import json
import sys
from pathlib import Path
from unittest.mock import MagicMock, patch
import pytest
import requests

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "OpenBISLabelStudio"))
import openbis_fetch


def test_empty_token_raises_invalid_token_error_without_using_cached_session():
    with patch.object(openbis_fetch, "Openbis") as mock_openbis:
        with pytest.raises(openbis_fetch.InvalidTokenError):
            openbis_fetch.create_openbis_connection("https://openbis.example.org", "   ")
    mock_openbis.assert_not_called()


def test_none_token_raises_invalid_token_error_without_using_cached_session():
    with patch.object(openbis_fetch, "Openbis") as mock_openbis:
        with pytest.raises(openbis_fetch.InvalidTokenError):
            openbis_fetch.create_openbis_connection("https://openbis.example.org", None)
    mock_openbis.assert_not_called()


def test_inactive_session_raises_invalid_token_error():
    client = MagicMock()
    client.is_session_active.return_value = False
    with patch.object(openbis_fetch, "Openbis", return_value=client):
        with pytest.raises(openbis_fetch.InvalidTokenError):
            openbis_fetch.create_openbis_connection("https://openbis.example.org", "tok-123")
    client.set_token.assert_called_once_with("tok-123", save_token=False)


def test_fallback_on_type_error_for_save_token():
    client = MagicMock()
    client.is_session_active.return_value = True

    def mock_set_token(token, **kwargs):
        if "save_token" in kwargs:
            raise TypeError("set_token() got an unexpected keyword argument 'save_token'")
        return None

    client.set_token.side_effect = mock_set_token
    with patch.object(openbis_fetch, "Openbis", return_value=client):
        conn = openbis_fetch.create_openbis_connection("https://openbis.example.org", "tok-123")
    assert conn == client
    assert client.set_token.call_count == 2
    client.set_token.assert_called_with("tok-123")


def test_fetch_sample_types_classifies_expired_token_and_connection_error():
    with patch.object(openbis_fetch, "Openbis") as mock_openbis:
        instance = mock_openbis.return_value
        instance.set_token.side_effect = ValueError("Session is no longer valid. Please log in again.")
        res = openbis_fetch.fetch_sample_types("https://openbis.example.org", "expired")
    assert res["success"] is False
    assert res["errorType"] == "token_expired"

    with patch.object(openbis_fetch, "Openbis", side_effect=requests.exceptions.ConnectionError("DNS failed")):
        res_net = openbis_fetch.fetch_sample_types("https://bad.invalid", "tok")
    assert res_net["success"] is False
    assert res_net["errorType"] == "connection_error"


def test_classify_error_handles_http_auth_and_bad_url():
    assert openbis_fetch.classify_error(ValueError("always use https!")) == "connection_error"
    assert openbis_fetch.classify_error(Exception("401 Unauthorized")) == "token_expired"
    assert openbis_fetch.classify_error(RuntimeError("unexpected boom")) == "unknown_error"


def test_substance_keeps_empa_batch_while_other_types_use_name():
    substance = MagicMock(permId="2026-1", type="SUBSTANCE", modificationDate="2026-11-13")
    substance.props.return_value = {"empa_number": "EMPA-42", "batch": "B", "name": "Ignored"}
    molecule = MagicMock(permId="2026-2", type="MOLECULE", modificationDate="2026-11-12")
    molecule.props.return_value = {"empa_number": "EMPA-99", "batch": "Z", "name": "Pentacene"}

    client = MagicMock()
    client.get_samples.side_effect = lambda type: [substance] if type == "SUBSTANCE" else [molecule]
    with patch.object(openbis_fetch, "create_openbis_connection", return_value=client):
        sub_res = openbis_fetch.fetch_samples_by_type("https://x", "t", "SUBSTANCE")
        mol_res = openbis_fetch.fetch_samples_by_type("https://x", "t", "MOLECULE")

    assert sub_res["data"][0]["name"] == "EMPA-42B"
    assert mol_res["data"][0]["name"] == "Pentacene"


def test_parse_cli_args_extracts_options_and_defaults():
    # Types without --out
    url, tok, mode, perm, out = openbis_fetch.parse_cli_args(["https://x", "t", "--types"])
    assert (url, tok, mode, perm, out) == ("https://x", "t", "--types", None, openbis_fetch.RESULTS_FILE)

    # Types with --out
    url, tok, mode, perm, out = openbis_fetch.parse_cli_args(["openbis_fetch.py", "https://x", "t", "--types", "--out", "out.json"])
    assert (url, tok, mode, perm, out) == ("https://x", "t", "--types", None, "out.json")

    # Sample type with --out
    url, tok, mode, perm, out = openbis_fetch.parse_cli_args(["https://x", "t", "SUBSTANCE", "--out", "sub.json"])
    assert (url, tok, mode, perm, out) == ("https://x", "t", "SUBSTANCE", None, "sub.json")

    # Details with --out after permId
    url, tok, mode, perm, out = openbis_fetch.parse_cli_args(["https://x", "t", "--details", "2026-1", "--out", "det.json"])
    assert (url, tok, mode, perm, out) == ("https://x", "t", "--details", "2026-1", "det.json")

    # Missing permId for --details raises ValueError
    with pytest.raises(ValueError, match="Missing perm_id"):
        openbis_fetch.parse_cli_args(["https://x", "t", "--details"])

    # Too few arguments raises ValueError
    with pytest.raises(ValueError):
        openbis_fetch.parse_cli_args(["https://x", "t"])


def test_cli_writes_to_out_path_instead_of_cwd(tmp_path, monkeypatch):
    out_file = tmp_path / "custom_out.json"
    monkeypatch.chdir(tmp_path)
    with patch.object(openbis_fetch, "fetch_sample_types", return_value={"success": True, "types": []}):
        exit_code = openbis_fetch.main(["openbis_fetch.py", "https://x", "tok", "--types", "--out", str(out_file)])
    assert exit_code == 0
    assert out_file.exists()
    assert not (tmp_path / openbis_fetch.RESULTS_FILE).exists()
    assert json.loads(out_file.read_text(encoding="utf-8")) == {"success": True, "types": []}


def test_cli_malformed_args_writes_error_json_and_returns_exit_1(tmp_path, monkeypatch):
    out_file = tmp_path / "err_out.json"
    monkeypatch.chdir(tmp_path)
    exit_code = openbis_fetch.main(["openbis_fetch.py", "--out", str(out_file)])
    assert exit_code == 1
    assert out_file.exists()
    data = json.loads(out_file.read_text(encoding="utf-8"))
    assert data["success"] is False
    assert data["errorType"] == "unknown_error"


def test_fetch_sample_details_and_by_type_classify_errors():
    with patch.object(openbis_fetch, "create_openbis_connection", side_effect=openbis_fetch.InvalidTokenError("Token invalid")):
        details_res = openbis_fetch.fetch_sample_details("https://x", "tok", "PERM-1")
        samples_res = openbis_fetch.fetch_samples_by_type("https://x", "tok", "SUBSTANCE")

    assert details_res["success"] is False
    assert details_res["errorType"] == "token_expired"
    assert samples_res["success"] is False
    assert samples_res["errorType"] == "token_expired"


def test_main_executes_sample_type_and_details_modes(tmp_path, monkeypatch):
    monkeypatch.chdir(tmp_path)
    out_samples = tmp_path / "samples.json"
    out_details = tmp_path / "details.json"

    with patch.object(openbis_fetch, "fetch_samples_by_type", return_value={"success": True, "data": []}):
        code1 = openbis_fetch.main(["openbis_fetch.py", "https://x", "tok", "SUBSTANCE", "--out", str(out_samples)])
    assert code1 == 0
    assert json.loads(out_samples.read_text(encoding="utf-8")) == {"success": True, "data": []}

    with patch.object(openbis_fetch, "fetch_sample_details", return_value={"success": True, "details": {}}):
        code2 = openbis_fetch.main(["openbis_fetch.py", "https://x", "tok", "--details", "PERM-1", "--out", str(out_details)])
    assert code2 == 0
    assert json.loads(out_details.read_text(encoding="utf-8")) == {"success": True, "details": {}}


def test_main_default_out_file_when_out_omitted(tmp_path, monkeypatch):
    monkeypatch.chdir(tmp_path)
    with patch.object(openbis_fetch, "fetch_sample_types", return_value={"success": True, "types": []}):
        code = openbis_fetch.main(["openbis_fetch.py", "https://x", "tok", "--types"])
    assert code == 0
    results_path = tmp_path / openbis_fetch.RESULTS_FILE
    assert results_path.exists()
    assert json.loads(results_path.read_text(encoding="utf-8")) == {"success": True, "types": []}


def test_main_no_arguments_writes_results_file_and_returns_1(tmp_path, monkeypatch):
    monkeypatch.chdir(tmp_path)
    code = openbis_fetch.main(["openbis_fetch.py"])
    assert code == 1
    results_path = tmp_path / openbis_fetch.RESULTS_FILE
    assert results_path.exists()
    data = json.loads(results_path.read_text(encoding="utf-8"))
    assert data["success"] is False
    assert data["errorType"] == "unknown_error"


def test_check_connection_uses_is_session_active_without_loading_sample_types(monkeypatch):
    resp_ok = MagicMock()
    resp_ok.json.return_value = {"jsonrpc": "2.0", "id": "1", "result": True}
    resp_ok.raise_for_status.return_value = None

    with patch.object(openbis_fetch.requests, "post", return_value=resp_ok) as mock_post:
        res_ok = openbis_fetch.check_connection("https://openbis.example.org/", "tok-valid")

    assert res_ok == {"success": True, "sessionActive": True}
    mock_post.assert_called_once()
    called_url = mock_post.call_args.args[0]
    called_json = mock_post.call_args.kwargs["json"]
    assert called_url == "https://openbis.example.org/openbis/openbis/rmi-application-server-v3.json"
    assert called_json["method"] == "isSessionActive"
    assert called_json["params"] == ["tok-valid"]

    resp_false = MagicMock()
    resp_false.json.return_value = {"jsonrpc": "2.0", "id": "1", "result": False}
    resp_false.raise_for_status.return_value = None
    with patch.object(openbis_fetch.requests, "post", return_value=resp_false):
        res_expired = openbis_fetch.check_connection("https://openbis.example.org", "tok-expired")
    assert res_expired["success"] is False
    assert res_expired["errorType"] == "token_expired"

    res_empty = openbis_fetch.check_connection("https://openbis.example.org", "   ")
    assert res_empty["success"] is False
    assert res_empty["errorType"] == "token_expired"

    res_http = openbis_fetch.check_connection("http://openbis.example.org", "tok")
    assert res_http["success"] is False
    assert res_http["errorType"] == "connection_error"


def test_fetch_sample_details_uses_cached_eln_preview_and_respects_skip_eln(tmp_path):
    cache_root = tmp_path / "openbis_eln_cache" / "DS-CACHE-1"
    cache_root.mkdir(parents=True)
    cached_png = cache_root / "structure.png"
    cached_png.write_bytes(b"png-bytes")

    parent_sample = MagicMock()
    parent_sample.type = "MOLECULE"
    ds = MagicMock()
    ds.type = "ELN_PREVIEW"
    ds.permId = "DS-CACHE-1"
    parent_sample.get_datasets.return_value = [ds]

    sample = MagicMock()
    sample.permId = "SMP-1"
    sample.type = "SUBSTANCE"
    sample.props.return_value = {"empa_number": "EMPA-1", "batch": "A"}
    sample.parents = ["MOLE-1"]

    client = MagicMock()
    client.get_sample.side_effect = lambda pid: sample if pid == "SMP-1" else parent_sample

    with patch.object(openbis_fetch, "create_openbis_connection", return_value=client):
        with patch.object(openbis_fetch.tempfile, "gettempdir", return_value=str(tmp_path)):
            res_cached = openbis_fetch.fetch_sample_details("https://x", "tok", "SMP-1")
            assert res_cached["success"] is True
            assert res_cached["details"]["elnPreviewPath"] == str(cached_png.resolve())
            ds.download.assert_not_called()

            client.get_sample.reset_mock()
            parent_sample.get_datasets.reset_mock()
            res_skipped = openbis_fetch.fetch_sample_details("https://x", "tok", "SMP-1", skip_eln=True)
            assert res_skipped["success"] is True
            assert res_skipped["details"]["elnPreviewPath"] == ""
            client.get_sample.assert_called_once_with("SMP-1")
            parent_sample.get_datasets.assert_not_called()


def test_main_dispatches_check_and_skip_eln_modes(tmp_path):
    out_check = tmp_path / "check.json"
    out_details = tmp_path / "details_skip.json"

    with patch.object(openbis_fetch, "check_connection", return_value={"success": True, "sessionActive": True}) as mock_check:
        code1 = openbis_fetch.main(["openbis_fetch.py", "https://x", "tok", "--check", "--out", str(out_check)])
    assert code1 == 0
    mock_check.assert_called_once_with("https://x", "tok")
    assert json.loads(out_check.read_text(encoding="utf-8")) == {"success": True, "sessionActive": True}

    with patch.object(openbis_fetch, "fetch_sample_details", return_value={"success": True, "details": {}}) as mock_det:
        code2 = openbis_fetch.main(["openbis_fetch.py", "https://x", "tok", "--details", "PERM-1", "--skip-eln", "--out", str(out_details)])
    assert code2 == 0
    mock_det.assert_called_once_with("https://x", "tok", "PERM-1", skip_eln=True)


