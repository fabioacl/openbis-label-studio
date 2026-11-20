# openBIS Label Studio

A Windows desktop application built with .NET 10 WinForms and Python `pybis` for querying openBIS laboratory inventory and rendering/routing formatted physical labels using the Brother b-PAC SDK and Brother PT-P700 label printers.

---

## Architecture Overview

The solution separates backend logic from the desktop UI across two main projects:

- **`OpenBISLabelStudio.Backend`** (`.NET 10` class library):
  - Owns all configuration, openBIS integration, label preview rendering, token recovery coordination, and b-PAC printing behind the `ILabelStudioBackend` facade (`LabelStudioBackend`).
  - Owns the Brother b-PAC COM reference (`bpac`), `Newtonsoft.Json`, `openbis_fetch.py`, and Brother `.lbx` templates (`templates/`).
  - Communicates with openBIS via a persistent `openbis_fetch.py --worker` stdio JSON loop in `OpenBisClient` (keeping the `pybis` session warm across calls, with transparent fallback to isolated one-shot `--out` CLI invocations).
- **`OpenBISLabelStudio`** (thin `.NET 10` WinForms frontend):
  - Contains only UI presentation and user interaction (`Program.cs`, `Form1`, `SetupSettingsForm`, `FormTokenRecoveryPrompter`), consuming `ILabelStudioBackend` with no direct dependency on `bpac` or `Newtonsoft.Json`.
  - Also hosts the out-of-process `--preview-worker` entry point in `Program.cs` (delegating to `LabelStudioBackend.RunPreviewWorkerLoop`).

---

## Features

- **Guided First-Run Setup Wizard (`SetupSettingsForm`)**:
  - Automatically runs on first launch (or whenever required configuration files under `config/` are missing/invalid):
    1. **Connection**: Enter your openBIS instance URL and Personal Access Token (`Show token` toggle + fast session check via `openbis_fetch.py --check` / `isSessionActive`).
    2. **Object Types & Template Profiles**: Filterable object type picker showing saved types immediately while syncing from openBIS asynchronously in the background with a loading bar. Each enabled type maps to **Molecule / Substance** (`Molecule`) or **Generic / Free-text** (`Generic`), while live example label previews render from `templates/*.lbx` in a pre-warmed `--preview-worker` process without blocking type selection.
    3. **Printer Routing**: Map Brother PT-P700 Windows printer queues to the **Small Box (12mm)**, **Magnet (12mm)**, and **Flask (24mm)** label slots (printer selectors filter strictly to `Brother PT-P700` queues with graceful fallback if none are attached).
  - Canceling initial setup while required configs are incomplete exits cleanly instead of opening an unconfigured main window.

- **openBIS Integration via `pybis` (`openbis_fetch.py` + `OpenBisClient`)**:
  - Runs primarily over a persistent `openbis_fetch.py --worker` stdio JSON loop (with isolated `--out` CLI fallback) to query available openBIS object types, samples sorted by latest modification date, and sample details (`studied_by` full name/username, storage location, supplier name, `Spreadsheet` evaporation temperature with units, special storage conditions, and cached `ELN_PREVIEW` images).
  - Switching object types in the main window displays a loading bar, cancels stale requests, and keeps the object-type selector and **Settings...** button responsive while samples load.
  - Classifies errors into `token_expired`, `connection_error`, and `unknown_error`.
  - **Expired-Token Recovery**: When startup or runtime queries fail with `token_expired`, `TokenRecoveryCoordinator` invokes `FormTokenRecoveryPrompter` (via `ITokenRecoveryPrompter`), pre-filling the openBIS URL, clearing/focusing the token input, validating and saving the new token, and automatically retrying the failed query.
  - **Offline Resilience**: If openBIS is unreachable on startup (`connection_error`), cached `displayName` entries from `config/openbis_types.json` remain usable alongside a non-fatal status banner.

- **Profile-Based Brother Label Templates (`OpenBISLabelStudio.Backend/templates/`)**:
  - Previews and print jobs run in an out-of-process, pre-warmed `OpenBISLabelStudio.exe --preview-worker` host (preventing b-PAC COM main-STA UI freezes). Previews crop exported bitmaps using `.lbx` XML `<style:paper>` dimensions and fall back internally to `Microsoft Print to PDF` if no Brother PT-P700 driver is installed (example previews are cached in `%TEMP%` after first render).
  - **Molecule profile (`Molecule`)**:
    - `template_molecule_box_12mm.lbx` — Sample name, PermID QR code, researcher username.
    - `template_molecule_magnet_12mm.lbx` — Sample name, PermID QR code, researcher full name, evaporation temperature, and special storage hazard/condition icons.
    - `template_molecule_flask_24mm.lbx` — Enlarged 24mm format with `ELN_PREVIEW` chemical structure, storage location, supplier name, and PermID QR code.
  - **Generic profile (`Generic`)**:
    - `template_small_box_12mm.lbx`, `template_magnet_12mm.lbx`, `template_flask_24mm.lbx` — Sample name, PermID QR code, and interactive custom **Free Text** notes on the 12mm Magnet and 24mm Flask labels with live preview re-rendering.

- **Ongoing Settings, Printer Auto-Save & Printing**:
  - Click **Settings...** on the main window at any time to edit Connection, Object Types & Templates, or Printer Routing (supports offline saving; **Cancel** discards unapplied changes).
  - Changing any of the three printer dropdowns on the main window immediately updates and saves `config/printers_config.json`.
  - Clicking **Print Labels** populates the checked `.lbx` templates and sends them through b-PAC to the selected installed Windows printer queues off the UI thread.

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
.
├── OpenBISLabelStudio.slnx                  # Visual Studio solution
├── OpenBISLabelStudio.Backend/              # Backend class library (owns bpac, Newtonsoft.Json, Python & templates)
│   ├── Contracts/                           # ILabelStudioBackend, ITokenRecoveryPrompter, backend DTOs
│   ├── Models/                              # ConfigModels (LabelTemplateProfile, ConfiguredOpenBisType, etc.)
│   ├── Services/                            # LabelStudioBackend, AppConfigService, OpenBisClient, LabelPreviewRenderer, TokenRecoveryCoordinator
│   ├── templates/                           # Tracked Brother .lbx templates (copied to output/templates/)
│   ├── OpenBISLabelStudio.Backend.csproj    # References bpac COM + Newtonsoft.Json; copies openbis_fetch.py & templates/
│   └── openbis_fetch.py                     # pybis worker (--worker) & CLI wrapper (--check, --types, <TYPE>, --details, --out)
├── OpenBISLabelStudio/                      # Thin WinForms frontend (references OpenBISLabelStudio.Backend only)
│   ├── Properties/PublishProfiles/          # Publish profile (FolderProfile.pubxml)
│   ├── Form1.cs / Form1.Designer.cs         # Main window orchestration
│   ├── FormTokenRecoveryPrompter.cs         # ITokenRecoveryPrompter WinForms adapter
│   ├── Program.cs                           # App entry point and --preview-worker host
│   ├── SetupSettingsForm.cs / .Designer.cs  # Unified Wizard, Expired-Token Recovery, and Settings dialog
│   └── OpenBISLabelStudio.csproj
├── OpenBISLabelStudio.Tests/                # C# unit and WinForms integration tests (Program.cs)
├── tests/                                   # Python pybis wrapper tests (test_openbis_fetch.py)
└── docs/superpowers/                        # Design specifications and implementation plans
```

### Runtime Configuration (`config/*.json`)

Runtime configuration files are stored in a git-ignored `config/` directory next to the executable (`AppDomain.CurrentDomain.BaseDirectory\config\`) and are created automatically by the setup wizard on first launch (any legacy root-level JSON files in the application directory are ignored). **`config/*.json` files are never tracked in Git and are never emitted by source builds or `MSBuild /t:Publish`.**

- `config/openbis_config.json` — openBIS instance URL and personal access token:
  ```json
  {
    "openbisUrl": "https://your-openbis-instance.org",
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

## Building, Testing & Publishing

> **Note:** Use `MSBuild.exe` rather than `dotnet build`, because `dotnet build` fails on the Brother b-PAC COM reference (`MSB4803`). Always pass `/t:Restore,Build` after cleaning `obj/`.

### Build (Debug & Release)

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" OpenBISLabelStudio.slnx /t:Restore,Build /p:Configuration=Debug /v:minimal
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" OpenBISLabelStudio.slnx /t:Restore,Build /p:Configuration=Release /v:minimal
```

### Run Tests

```powershell
# C# unit & WinForms integration tests (Debug & Release)
& ".\OpenBISLabelStudio.Tests\bin\Debug\net10.0-windows\OpenBISLabelStudio.Tests.exe"
& ".\OpenBISLabelStudio.Tests\bin\Release\net10.0-windows\OpenBISLabelStudio.Tests.exe"

# Python openbis_fetch.py unit & worker tests
pytest tests/test_openbis_fetch.py -v
```

### Publish (Release)

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" OpenBISLabelStudio\OpenBISLabelStudio.csproj /t:Publish /p:Configuration=Release /p:PublishDir="C:\path\to\deploy-folder\" /v:minimal
```

The published folder contains the runtime deliverables — `OpenBISLabelStudio.exe`, `OpenBISLabelStudio.Backend.dll`, `Newtonsoft.Json.dll`, `openbis_fetch.py`, and `templates\*.lbx` (plus `.NET` host/runtime files such as `OpenBISLabelStudio.dll`, `OpenBISLabelStudio.deps.json`, `OpenBISLabelStudio.runtimeconfig.json`) — and **no** `config\` directory or secret JSON files.
