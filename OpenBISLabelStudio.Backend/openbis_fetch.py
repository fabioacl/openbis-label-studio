from collections import OrderedDict
import contextlib
import io
import sys
import json
import os
import re
import socket
import tempfile
from urllib.parse import urlparse
import urllib3
import requests

# Disable insecure HTTPS warnings if self-signed certs are used
urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)

RESULTS_FILE = "openbis_results.json"
IMAGE_EXTENSIONS = (".png", ".jpg", ".jpeg", ".bmp", ".gif")

# Lazily imported when full pyBIS queries run so fast checks (--check) avoid importing pandas/pybis.
Openbis = None


def _get_openbis_class():
    global Openbis
    if Openbis is None:
        from pybis import Openbis as pybis_openbis
        Openbis = pybis_openbis
    return Openbis


class InvalidTokenError(Exception):
    """Raised when an openBIS token is empty, invalid, or expired."""
    pass


def classify_error(exc: Exception) -> str:
    """
    Classifies an exception into:
      - 'token_expired'
      - 'connection_error'
      - 'unknown_error'
    """
    if isinstance(exc, InvalidTokenError):
        return "token_expired"

    if isinstance(exc, (requests.exceptions.RequestException, urllib3.exceptions.HTTPError, socket.error)):
        response = getattr(exc, "response", None)
        if response is not None and getattr(response, "status_code", None) in (401, 403):
            return "token_expired"
        return "connection_error"

    msg = str(exc).lower()

    if isinstance(exc, ValueError):
        if any(w in msg for w in ("url", "hostname", "https")):
            return "connection_error"
        if re.search(r'\b(session|token|valid|expired|login)\b', msg) or "invalid session" in msg or "no longer valid" in msg:
            return "token_expired"

    # Keywords in error message for general exceptions
    token_keywords = [
        "401", "403", "unauthorized", "authentication", "invalid session",
        "session expired", "token expired", "token is empty", "token invalid",
        "invalid token", "no longer valid"
    ]
    if any(k in msg for k in token_keywords) or re.search(r'\b(token|session|unauthorized)\b', msg):
        return "token_expired"

    conn_keywords = [
        "connection", "timeout", "timed out", "ssl", "name resolution",
        "getaddrinfo", "unreachable", "refused", "reset", "dns",
        "network", "https", "hostname"
    ]
    if any(k in msg for k in conn_keywords):
        return "connection_error"

    return "unknown_error"


def create_openbis_connection(openbis_url, token):
    if not token or not str(token).strip():
        raise InvalidTokenError("Personal access token is empty.")

    openbis_cls = _get_openbis_class()
    o = openbis_cls(openbis_url, verify_certificates=False)
    try:
        o.set_token(token, save_token=False)
    except TypeError:
        # Fallback for pybis versions where save_token argument is not accepted
        o.set_token(token)

    if not o.is_session_active():
        raise InvalidTokenError("Session token is expired or invalid.")

    return o


def _normalize_openbis_url(openbis_url: str) -> str:
    raw = str(openbis_url or "").strip()
    if not raw:
        raise ValueError("please provide a URL you want to connect to.")

    if not raw.startswith("http"):
        raw = "https://" + raw

    parsed = urlparse(raw)
    if not parsed.netloc or not parsed.hostname:
        raise ValueError("please provide the url in this format: https://openbis.host.ch:8443")
    if parsed.scheme == "http":
        raise ValueError("always use https!")
    if parsed.scheme != "https":
        raise ValueError("please provide the url in this format: https://openbis.host.ch:8443")

    return parsed.geturl().rstrip("/")


def check_connection(openbis_url, token):
    """
    Fast openBIS session verification via JSON-RPC isSessionActive without importing pybis
    or enumerating object types.
    """
    try:
        if not token or not str(token).strip():
            raise InvalidTokenError("Personal access token is empty.")

        base_url = _normalize_openbis_url(openbis_url)
        endpoint = base_url + "/openbis/openbis/rmi-application-server-v3.json"
        payload = {
            "id": "1",
            "jsonrpc": "2.0",
            "method": "isSessionActive",
            "params": [str(token).strip()],
        }

        resp = requests.post(endpoint, json=payload, verify=False, timeout=15)
        resp.raise_for_status()
        data = resp.json()

        if isinstance(data, dict) and data.get("error"):
            err = data["error"]
            err_msg = err.get("message") if isinstance(err, dict) else str(err)
            raise Exception(err_msg or "openBIS connection check failed.")

        if isinstance(data, dict) and bool(data.get("result")):
            return {"success": True, "sessionActive": True}

        raise InvalidTokenError("Session token is expired or invalid.")
    except Exception as e:
        return {"success": False, "errorType": classify_error(e), "error": str(e)}


def extract_evaporation_temperature(spreadsheet):
    """
    Extracts the evaporation temperature directly using the pyBIS Spreadsheet API.
    """
    if spreadsheet is None:
        return ""

    headers = getattr(spreadsheet, "headers", None)
    data = getattr(spreadsheet, "data", None)

    if not headers or not data:
        return ""

    # Find the column title strictly matching Temperature [Unit] (e.g. Temperature [K], Temperature [°C])
    temp_col_idx = -1
    unit = ""

    for idx, h in enumerate(headers):
        if not h:
            continue
        m = re.search(r'temperature\s*\[(.*?)\]', str(h).strip(), re.IGNORECASE)
        if m:
            temp_col_idx = idx
            unit = m.group(1).strip()
            break

    # If the column title is not specified with Temperature [Unit], do not extract
    if temp_col_idx == -1:
        return ""

    # Extract the value from the last row with a non-empty value in this column
    for row in reversed(data):
        if not isinstance(row, list) or temp_col_idx >= len(row):
            continue
        cell_val = str(row[temp_col_idx]).strip()
        if cell_val:
            if unit and unit.lower() not in cell_val.lower():
                return f"{cell_val} {unit}"
            return cell_val

    return ""


def _is_truthy_flag(value):
    """Interpret boolean/DataFrame cell flags cleanly, treating NaN/NA/None as False."""
    if value is None or isinstance(value, bool):
        return bool(value)
    if isinstance(value, str):
        return value.strip().lower() in ("true", "1", "yes")
    try:
        if value != value:
            return False
        return bool(value)
    except (TypeError, ValueError):
        return False


def fetch_sample_types(openbis_url, token, session=None):
    try:
        o = session if session is not None else create_openbis_connection(openbis_url, token)
        types = o.get_sample_types()
        df = types.df
        type_list = []
        for idx, row in df.iterrows():
            if _is_truthy_flag(row.get("managedInternally")):
                continue
            code = str(row['code'])
            desc = str(row.get('description') or '').strip()
            if not desc or desc.lower() == 'none':
                desc = code.replace('_', ' ').title()
            type_list.append({
                "code": code,
                "description": desc
            })
        return {"success": True, "types": type_list}
    except Exception as e:
        return {"success": False, "errorType": classify_error(e), "error": str(e)}


def fetch_samples_by_type(openbis_url, token, sample_type, session=None):
    try:
        o = session if session is not None else create_openbis_connection(openbis_url, token)
        samples = o.get_samples(type=sample_type)

        result_list = []
        for sample in samples:
            props = sample.props() if callable(getattr(sample, 'props', None)) else getattr(sample, 'props', {})
            if not isinstance(props, dict):
                try:
                    props = dict(props)
                except Exception:
                    props = {}

            if sample_type == "SUBSTANCE":
                empa = str(props.get('empa_number') or '').strip()
                batch = str(props.get('batch') or '').strip()
                if empa or batch:
                    name = f"{empa}{batch}"
                else:
                    name = str(props.get('name') or sample.permId or '')
            else:
                name = str(props.get('name') or sample.permId or '')

            mod_date = str(getattr(sample, 'modificationDate', '') or '')
            result_list.append({
                "name": name,
                "permId": str(sample.permId or ""),
                "modificationDate": mod_date
            })

        # Sort by modificationDate descending (latest modified first)
        result_list.sort(key=lambda x: str(x.get("modificationDate") or x.get("permId") or ""), reverse=True)

        return {"success": True, "data": result_list}

    except Exception as e:
        return {"success": False, "errorType": classify_error(e), "error": str(e)}


def _find_first_image_file(directory: str) -> str:
    if not directory or not os.path.exists(directory):
        return ""

    for root, _, files in os.walk(directory):
        for file in sorted(files):
            if file.lower().endswith(IMAGE_EXTENSIONS):
                return os.path.abspath(os.path.join(root, file))

    return ""


def fetch_sample_details(openbis_url, token, perm_id, skip_eln: bool = False, session=None):
    try:
        o = session if session is not None else create_openbis_connection(openbis_url, token)

        # Get specific sample by permId
        sample = o.get_sample(perm_id)
        sample_type = str(sample.type or "").upper()
        props = sample.props() if callable(getattr(sample, 'props', None)) else getattr(sample, 'props', {})
        if not isinstance(props, dict):
            try:
                props = dict(props)
            except Exception:
                props = {}

        # 1. Determine Display Name
        if sample_type == "SUBSTANCE":
            empa = str(props.get('empa_number') or '').strip()
            batch = str(props.get('batch') or '').strip()
            if empa or batch:
                name = f"{empa}{batch}"
            else:
                name = str(props.get('name') or sample.permId or '')
        else:
            name = str(props.get('name') or sample.permId or '')

        # 2. Resolve studied_by Person (both full name and username)
        studied_by_name = ""
        studied_by_username = ""
        sb = props.get('studied_by')
        if sb:
            person_id = None
            if isinstance(sb, list) and len(sb) > 0:
                person_id = sb[0]
            elif isinstance(sb, str) and sb.strip():
                person_id = sb.strip()

            if person_id:
                try:
                    person_sample = o.get_sample(person_id)
                    p_props = person_sample.props() if callable(getattr(person_sample, 'props', None)) else getattr(person_sample, 'props', {})
                    studied_by_name = str(p_props.get('name') or person_sample.permId or "")
                    studied_by_username = str(p_props.get('username') or p_props.get('user_id') or p_props.get('user') or p_props.get('name') or person_sample.permId or "")
                except Exception:
                    studied_by_name = str(person_id)
                    studied_by_username = str(person_id)

        # 3. Resolve Storage Location & Supplier Own Name
        storage_location = str(props.get('storage_field') or props.get('storage_location') or "").strip()
        if not storage_location and props.get('location'):
            loc_id = props.get('location')
            try:
                loc_sample = o.get_sample(loc_id)
                l_props = loc_sample.props() if callable(getattr(loc_sample, 'props', None)) else getattr(loc_sample, 'props', {})
                storage_location = str(l_props.get('name') or loc_id)
            except Exception:
                storage_location = str(loc_id)

        supplier_own_name = str(props.get('supplier_own_name') or "").strip()

        # 4. Extract Evaporation Temperature using pyBIS Spreadsheet API
        spreadsheet = sample.props.evaporation_temperatures if hasattr(sample.props, 'evaporation_temperatures') else None
        evaporation_temperature = extract_evaporation_temperature(spreadsheet)

        # 5. Parse Special Storage Conditions & Icon Flags
        raw_cond = props.get('special_storage_conditions')
        cond_list = []
        if isinstance(raw_cond, list):
            cond_list = [str(c).strip().upper() for c in raw_cond if c]
        elif isinstance(raw_cond, str) and raw_cond.strip():
            cond_list = [c.strip().upper() for c in raw_cond.split(',') if c.strip()]

        icon_flags = {
            "poisonous": "POISONOUS" in cond_list,
            "flammable": "FLAMMABLE" in cond_list,
            "dark": "DARK" in cond_list,
            "dry": "DRY" in cond_list,
            "freezer": "FREEZER" in cond_list,
            "fridge": "FRIDGE" in cond_list,
            "noOxygen": "NO_OXYGEN" in cond_list or "NOOXYGEN" in cond_list,
            "tightlyClosed": "TIGHTLY_CLOSED" in cond_list or "TIGHTLY_CLOSE" in cond_list or "TIGHTLYCLOSED" in cond_list
        }

        # 6. Parent MOLECULE & ELN Preview Image Download
        eln_preview_path = ""
        if not skip_eln and sample.parents:
            molecule_parent = None
            for p in sample.parents:
                try:
                    p_sample = o.get_sample(p) if isinstance(p, str) else p
                    p_type = str(getattr(p_sample, 'type', '')).upper()
                    if "MOLECULE" in p_type:
                        molecule_parent = p_sample
                        break
                    if molecule_parent is None:
                        molecule_parent = p_sample
                except Exception:
                    pass

            if molecule_parent:
                try:
                    datasets = molecule_parent.get_datasets()
                    for ds in datasets:
                        if str(getattr(ds, 'type', '')).upper() == "ELN_PREVIEW":
                            temp_dir = os.path.join(tempfile.gettempdir(), "openbis_eln_cache")
                            ds_dir = os.path.join(temp_dir, ds.permId)
                            eln_preview_path = _find_first_image_file(ds_dir)
                            if not eln_preview_path:
                                os.makedirs(temp_dir, exist_ok=True)
                                ds.download(destination=temp_dir, wait_until_finished=True)
                                eln_preview_path = _find_first_image_file(ds_dir)
                            break
                except Exception:
                    pass

        details = {
            "permId": str(sample.permId or ""),
            "sampleType": sample_type,
            "name": str(name),
            "studiedBy": str(studied_by_name),
            "studiedByName": str(studied_by_name),
            "studiedByUsername": str(studied_by_username),
            "storageLocation": str(storage_location),
            "supplierOwnName": str(supplier_own_name),
            "evaporationTemperature": str(evaporation_temperature),
            "specialStorageConditions": cond_list,
            "iconFlags": icon_flags,
            "elnPreviewPath": str(eln_preview_path),
            "properties": {k: str(v) for k, v in props.items() if v is not None}
        }

        return {"success": True, "details": details}

    except Exception as e:
        return {"success": False, "errorType": classify_error(e), "error": str(e)}


def parse_cli_args(argv: list[str]) -> tuple[str, str, str, str | None, str]:
    """
    Parses CLI arguments.
    Usage:
      python openbis_fetch.py <url> <token> (--check | --types | <sample_type> | --details <perm_id> [--skip-eln]) [--out <json_path>]
    Returns:
      (url, token, mode_or_type, perm_id, out_path)
    """
    args = [arg for arg in argv if arg != "--skip-eln"]
    if args and (args[0].endswith(".py") or args[0] in ("python", "python3") or args[0].endswith("python.exe")):
        args = args[1:]

    out_path = RESULTS_FILE
    if "--out" in args:
        idx = args.index("--out")
        if idx + 1 >= len(args):
            raise ValueError("Missing path for --out argument.")
        out_path = args[idx + 1]
        del args[idx:idx + 2]

    if len(args) < 3:
        raise ValueError("Invalid arguments provided to Python script.")

    url = args[0]
    token = args[1]
    mode_or_type = args[2]

    perm_id = None
    if mode_or_type == "--details":
        if len(args) < 4:
            raise ValueError("Missing perm_id for --details.")
        perm_id = args[3]

    return url, token, mode_or_type, perm_id, out_path


class WorkerSessionCache:
    """Caches authenticated pyBIS Openbis clients keyed by (url, token) for worker reuse."""

    def __init__(self, max_entries: int = 8):
        self._max_entries = max(1, max_entries)
        self._sessions: OrderedDict[tuple[str, str], object] = OrderedDict()

    def _key(self, url: str, token: str) -> tuple[str, str]:
        return (str(url or "").strip(), str(token or ""))

    def clear(self) -> None:
        self._sessions.clear()

    def invalidate(self, url: str, token: str) -> None:
        self._sessions.pop(self._key(url, token), None)

    def execute(self, url: str, token: str, fn) -> dict:
        """
        Executes fn(session) using a cached session when present.
        If a cached session fails, evicts it immediately and retries once with a fresh session
        unless the failure is an expired/invalid token.
        """
        key = self._key(url, token)
        cached_session = self._sessions.get(key)

        if cached_session is not None:
            self._sessions.move_to_end(key)
            try:
                result = fn(cached_session)
            except Exception as exc:
                result = {"success": False, "errorType": classify_error(exc), "error": str(exc)}

            if result.get("success"):
                return result

            self._sessions.pop(key, None)
            if result.get("errorType") == "token_expired":
                return result

        try:
            session = create_openbis_connection(url, token)
            result = fn(session)
        except Exception as exc:
            return {"success": False, "errorType": classify_error(exc), "error": str(exc)}

        if result.get("success"):
            self._sessions[key] = session
            self._sessions.move_to_end(key)
            while len(self._sessions) > self._max_entries:
                self._sessions.popitem(last=False)

        return result


def _dispatch_worker_request(req: dict, session_cache: WorkerSessionCache) -> dict:
    action = req.get("action")
    url = str(req.get("url") or "")
    token = str(req.get("token") or "")

    if action == "check":
        return check_connection(url, token)
    if action == "types":
        return session_cache.execute(
            url,
            token,
            lambda s: fetch_sample_types(url, token, session=s),
        )
    if action == "list":
        sample_type = str(req.get("sampleType") or "")
        if not sample_type:
            raise ValueError("Missing sampleType for list action.")
        return session_cache.execute(
            url,
            token,
            lambda s: fetch_samples_by_type(url, token, sample_type, session=s),
        )
    if action == "details":
        perm_id = str(req.get("permId") or "")
        if not perm_id:
            raise ValueError("Missing permId for details action.")
        skip_eln = bool(req.get("skipEln", False))
        return session_cache.execute(
            url,
            token,
            lambda s: fetch_sample_details(url, token, perm_id, skip_eln=skip_eln, session=s),
        )

    raise ValueError(f"Unsupported worker action: {action!r}")


def run_worker_loop(stdin_stream=None, stdout_stream=None) -> int:
    """
    Runs the persistent single-line JSON stdio worker loop until EOF.
    Reads one JSON request per line, dispatches openBIS queries reusing cached sessions,
    and writes one JSON response line per request without printing stray stdout text.
    """
    in_stream = stdin_stream if stdin_stream is not None else sys.stdin
    out_stream = stdout_stream if stdout_stream is not None else sys.stdout
    session_cache = WorkerSessionCache()
    redirect_target = sys.stderr if sys.stderr is not None else io.StringIO()

    while True:
        line = in_stream.readline()
        if line == "":
            break

        stripped = line.lstrip("\ufeff").strip()
        if not stripped:
            continue

        try:
            req = json.loads(stripped)
            if not isinstance(req, dict):
                raise ValueError("Worker request must be a JSON object.")
        except (json.JSONDecodeError, ValueError) as exc:
            result = {
                "success": False,
                "errorType": "unknown_error",
                "error": f"Invalid JSON request: {exc}",
            }
        else:
            try:
                with contextlib.redirect_stdout(redirect_target):
                    result = _dispatch_worker_request(req, session_cache)
            except Exception as exc:
                result = {
                    "success": False,
                    "errorType": classify_error(exc),
                    "error": str(exc),
                }

        out_stream.write(json.dumps(result, ensure_ascii=False) + "\n")
        out_stream.flush()

    return 0


def main(argv: list[str] | None = None) -> int:
    """
    Main entry point for CLI and --worker execution.
    In CLI mode writes JSON results to out_path and returns exit code:
      - 0 on successful fetch execution (even if openBIS returns an error object)
      - 1 on malformed arguments or unhandled script failure
    """
    if argv is None:
        argv = sys.argv

    args_without_exe = list(argv)
    if args_without_exe:
        first_lower = args_without_exe[0].lower()
        if first_lower.endswith(".py") or first_lower in ("python", "python3") or first_lower.endswith("python.exe"):
            args_without_exe = args_without_exe[1:]

    if args_without_exe and args_without_exe[0] == "--worker":
        for stream in (sys.stdin, sys.stdout):
            reconfigure = getattr(stream, "reconfigure", None)
            if callable(reconfigure):
                try:
                    reconfigure(encoding="utf-8", errors="replace")
                except Exception:
                    pass
        return run_worker_loop(sys.stdin, sys.stdout)

    skip_eln = "--skip-eln" in argv

    # Detect --out early if present, so argument errors can be written to the requested path
    out_path = RESULTS_FILE
    if "--out" in argv:
        try:
            idx = argv.index("--out")
            if idx + 1 < len(argv):
                out_path = argv[idx + 1]
        except Exception:
            pass

    try:
        url, token, mode_or_type, perm_id, parsed_out_path = parse_cli_args(argv)
        out_path = parsed_out_path
    except Exception as e:
        error_payload = {
            "success": False,
            "errorType": classify_error(e),
            "error": str(e)
        }
        try:
            with open(out_path, "w", encoding="utf-8") as f:
                json.dump(error_payload, f, indent=2)
        except Exception:
            pass
        return 1

    try:
        if mode_or_type == "--check":
            result = check_connection(url, token)
        elif mode_or_type == "--types":
            result = fetch_sample_types(url, token)
        elif mode_or_type == "--details":
            result = fetch_sample_details(url, token, perm_id, skip_eln=True) if skip_eln else fetch_sample_details(url, token, perm_id)
        else:
            sample_type = mode_or_type
            result = fetch_samples_by_type(url, token, sample_type)
    except Exception as e:
        result = {
            "success": False,
            "errorType": classify_error(e),
            "error": str(e)
        }

    try:
        with open(out_path, "w", encoding="utf-8") as f:
            json.dump(result, f, indent=2)
    except Exception:
        return 1

    return 0


if __name__ == "__main__":
    sys.exit(main())
