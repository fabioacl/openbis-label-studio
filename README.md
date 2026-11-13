# openBIS Label Studio

A Windows desktop application built with .NET 10 WinForms and Python `pybis` for querying openBIS laboratory inventory and rendering/routing formatted physical labels using the Brother b-PAC SDK and Brother PT-P700 label printers.

---

## Features

- **Guided First-Run Setup Wizard (`SetupSettingsForm`)**:
  - Automatically runs on first launch (or whenever required configuration files under `config/` are missing/invalid):
    1. **Connection**: Enter your openBIS instance URL and Personal Access Token (`Show token` toggle + live **Test Connection** check via `openbis_fetch.py --types`).
    2. **Object Types & Template Profiles**: Filterable object type picker fetched from openBIS where each enabled type is mapped to either **Molecule / Substance** (`Molecule`) or **Generic / Free-text** (`Generic`), with live example label previews rendered directly from `templates/*.lbx`.
    3. **Printer Routing**: Map Windows printer queues to the **Small Box (12mm)**, **Magnet (12mm)**, and **Flask (24mm)** label slots (with graceful fallback if no printers are detected).
  - Canceling initial setup while required configs are incomplete exits cleanly instead of opening an unconfigured main window.

- **openBIS Integration via `pybis` (`openbis_fetch.py` + `OpenBisClient`)**:
  - Queries available openBIS object types, samples sorted by latest modification date, and sample details (`studied_by` full name/username, storage location, supplier name, `Spreadsheet` evaporation temperature with units, special storage conditions, and parent `ELN_PREVIEW` images).
  - Writes Python query outputs to isolated temporary files (`--out`) and classifies errors into `token_expired`, `connection_error`, and `unknown_error`.
  - **Expired-Token Recovery**: When startup or runtime queries fail with `token_expired`, an inline recovery prompt pre-fills the openBIS URL, clears/focuses the token input, validates and saves the new token, and automatically retries the failed query.
  - **Offline Resilience**: If openBIS is unreachable on startup (`connection_error`), cached `displayName` entries from `config/openbis_types.json` remain usable alongside a non-fatal status banner.

- **Profile-Based Brother Label Templates (`OpenBISLabelStudio/templates/`)**:
  - **Molecule profile (`Molecule`)**:
    - `template_molecule_box_12mm.lbx` — Sample name, PermID QR code, researcher username.
    - `template_molecule_magnet_12mm.lbx` — Sample name, PermID QR code, researcher full name, evaporation temperature, and special storage hazard/condition icons.
    - `template_molecule_flask_24mm.lbx` — Enlarged 24mm format with `ELN_PREVIEW` chemical structure, storage location, supplier name, and PermID QR code.
  - **Generic profile (`Generic`)**:
    - `template_small_box_12mm.lbx`, `template_magnet_12mm.lbx`, `template_flask_24mm.lbx` — Sample name, PermID QR code, and interactive custom **Free Text** notes on the 12mm Magnet and 24mm Flask labels with live preview re-rendering.

- **Ongoing Settings & Printer Auto-Save**:
  - Click **Settings...** on the main window at any time to edit Connection, Object Types & Templates, or Printer Routing (supports offline saving; **Cancel** discards unapplied changes).
  - Changing any of the three printer dropdowns on the main window immediately updates and saves `config/printers_config.json`.
  - Clicking **Print Labels** displays the configured printer queue and resolved `.lbx` template filenames in a **Printer Routing & Queue** confirmation dialog (direct physical printing remains paused).

---

## Prerequisites

1. **Windows 10 / 11 (x64)**
2. **.NET 10 SDK & Desktop Runtime**
3. **Visual Studio 2022/2026 (or MSBuild Build Tools)** for resolving the Brother b-PAC COM reference (`bpac` via `tlbimp`)
4. **Python 3.9+** with `pybis` (and `pytest` for running Python unit tests):
   ```powershell
   pip install pybis pytest
   ```
5. **Brother b-PAC SDK** (version 3.1+ x64 COM component)
6. **Brother PT-P700 Printer Drivers**

---

## Project Layout & Configuration

```text
OpenBISLabelStudio/
├── Models/                 # DTOs and enums (LabelTemplateProfile, ConfiguredOpenBisType, etc.)
├── Services/               # AppConfigService, OpenBisClient, LabelPreviewRenderer
├── templates/              # Tracked Brother .lbx label templates (copied to output/templates/)
├── Form1.cs                # Main window orchestration
├── SetupSettingsForm.cs    # Unified Wizard, Expired-Token Recovery, and Settings dialog
├── openbis_fetch.py        # pybis CLI wrapper (--types, <TYPE>, --details <PERM_ID>, --out)
└── openbis_config.example.json
```

Runtime configuration files are stored in a git-ignored `config/` directory next to the executable (`AppDomain.CurrentDomain.BaseDirectory\config\`) and are created automatically by the setup wizard:

- `config/openbis_config.json` — openBIS instance URL and personal access token (see `OpenBISLabelStudio/openbis_config.example.json` for schema reference):
  ```json
  {
    "openbisUrl": "https://openbis.your-institution.ch",
    "token": "YOUR_OPENBIS_PERSONAL_ACCESS_TOKEN"
  }
  ```
- `config/openbis_types.json` — Enabled openBIS object types, cached display names, and template profiles (`"Molecule"` or `"Generic"`):
  ```json
  [
    {
      "code": "SUBSTANCE",
      "displayName": "Substance",
      "templateProfile": "Molecule"
    },
    {
      "code": "CRYSTAL",
      "displayName": "Crystal",
      "templateProfile": "Generic"
    }
  ]
  ```
- `config/printers_config.json` — Local Windows printer queue mappings:
  ```json
  {
    "smallBoxPrinter": "Brother PT-P700",
    "magnetPrinter": "Brother PT-P700 (Kopie 1)",
    "flaskPrinter": "Brother PT-P700 (Kopie 2)"
  }
  ```

---

## Building & Testing

> **Note:** Use `MSBuild.exe` rather than `dotnet build`, because `dotnet build` fails on the Brother b-PAC COM reference (`MSB4803`). Always pass `/t:Restore,Build` after cleaning `obj/`.

### Build (Debug & Release)

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" OpenBISLabelStudio.slnx /t:Restore,Build /p:Configuration=Debug /nologo
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" OpenBISLabelStudio.slnx /t:Restore,Build /p:Configuration=Release /nologo
```

### Run Tests

```powershell
# C# unit & WinForms integration tests
& "OpenBISLabelStudio.Tests\bin\Debug\net10.0-windows\OpenBISLabelStudio.Tests.exe"

# Python openbis_fetch.py unit tests
python -m pytest tests/test_openbis_fetch.py -v
```
