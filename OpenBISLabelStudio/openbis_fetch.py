import sys
import json
import os
import re
import socket
import tempfile
import urllib3
import requests
from pybis import Openbis

# Disable insecure HTTPS warnings if self-signed certs are used
urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)

RESULTS_FILE = "openbis_results.json"


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

    o = Openbis(openbis_url, verify_certificates=False)
    try:
        o.set_token(token, save_token=False)
    except TypeError:
        # Fallback for pybis versions where save_token argument is not accepted
        o.set_token(token)

    if not o.is_session_active():
        raise InvalidTokenError("Session token is expired or invalid.")

    return o


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


def fetch_sample_types(openbis_url, token):
    try:
        o = create_openbis_connection(openbis_url, token)
        types = o.get_sample_types()
        df = types.df
        type_list = []
        for idx, row in df.iterrows():
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


def fetch_samples_by_type(openbis_url, token, sample_type):
    try:
        o = create_openbis_connection(openbis_url, token)
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


def fetch_sample_details(openbis_url, token, perm_id):
    try:
        o = create_openbis_connection(openbis_url, token)

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
        molecule_parent = None
        if sample.parents:
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
                        os.makedirs(temp_dir, exist_ok=True)
                        ds.download(destination=temp_dir, wait_until_finished=True)
                        ds_dir = os.path.join(temp_dir, ds.permId)
                        if os.path.exists(ds_dir):
                            for root, dirs, files in os.walk(ds_dir):
                                for file in files:
                                    if file.lower().endswith(('.png', '.jpg', '.jpeg', '.bmp', '.gif')):
                                        eln_preview_path = os.path.abspath(os.path.join(root, file))
                                        break
                                if eln_preview_path:
                                    break
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
      python openbis_fetch.py <url> <token> (--types | <sample_type> | --details <perm_id>) [--out <json_path>]
    Returns:
      (url, token, mode_or_type, perm_id, out_path)
    """
    args = list(argv)
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


def main(argv: list[str] | None = None) -> int:
    """
    Main entry point for CLI execution. Writes JSON results to out_path and returns exit code:
      - 0 on successful fetch execution (even if openBIS returns an error object)
      - 1 on malformed arguments or unhandled script failure
    """
    if argv is None:
        argv = sys.argv

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
        if mode_or_type == "--types":
            result = fetch_sample_types(url, token)
        elif mode_or_type == "--details":
            result = fetch_sample_details(url, token, perm_id)
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
