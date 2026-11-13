# Design: Configuration Directory, First-Run Setup, and Template Mapping

**Date:** 2026-11-13
**Project:** `OpenBISLabelStudio`
**Status:** Ready for Approval

---

## 1. Intent & Why

Today, `OpenBISLabelStudio` keeps configuration files and `.lbx` label templates mixed flat in the project/application root, tracks `openbis_types.json` and `printers_config.json` in git, silently creates placeholder configs on first launch, and hardcodes `SUBSTANCE` vs. non-substance template selection in `Form1.cs`.

This change makes the application clean to share in git and effortless to set up on a new machine:
- Sensitive and machine-specific data (openBIS URL, personal access token, selected openBIS types, template mappings, and local printer queues) live inside a git-ignored `config/` directory.
- Label template files (`.lbx`) live inside a tracked `templates/` directory.
- On first launch (or whenever required configs are missing or a token expires), the user is guided through connecting to openBIS, selecting object types with live rendered label previews, and mapping printers per layout. Once configured, the app opens directly into normal use and never prompts again unless a token expires or the user clicks **Settings**.

---

## 2. Goals, Non-Goals, and Anti-Goals

### Goals
1. **Directory separation & git hygiene:**
   - Move `openbis_config.json`, `openbis_types.json`, and `printers_config.json` into a `config/` folder next to the executable (mirrored from `OpenBISLabelStudio/config/` if present locally), and ignore `config/` and `**/config/` in `.gitignore`.
   - Remove the currently tracked `OpenBISLabelStudio/openbis_types.json` and `OpenBISLabelStudio/printers_config.json` from git tracking, and remove the unused hardcoded lab path `PRECURSORS_COLLECTION` from `OpenBISLabelStudio/openbis_fetch.py`.
   - Move all 6 `.lbx` files into `OpenBISLabelStudio/templates/` (tracked in git and copied to `<output>/templates/` on build/publish).
   - Write temporary Python query outputs to a unique file in the OS temp folder instead of `openbis_results.json` in the application directory.
2. **First-run setup & expired-token recovery:**
   - If `config/openbis_config.json` is missing or incomplete (`openbisUrl` or `token` empty), ask for Instance URL and Personal Access Token, then test the connection against openBIS.
   - Clearly distinguish expired/invalid token failures from URL/network/Python failures. On expired/invalid token (during first run, startup, or runtime queries), report the problem to the user, pre-fill the URL, and ask for a new token before persisting it.
   - If `config/openbis_types.json` is missing, fetch the server's object types from openBIS and let the user choose which types to enable and which template profile (`Molecule` or `Generic`) each type uses, showing live rendered example previews from `templates/`.
   - If `config/printers_config.json` is missing, let the user select which installed Windows printer handles each layout (`Small Box 12mm`, `Magnet 12mm`, `Flask 24mm`).
3. **Config-driven runtime & ongoing control:**
   - Replace the hardcoded `IsSubstance()` check in `Form1.cs` with the configured template profile for each type in `config/openbis_types.json`.
   - Add a **Settings** button in `Form1` to reopen the setup/settings dialog anytime for Connection, Object Types & Templates, or Printer Routing.
   - Auto-save changes made to the three printer dropdowns on `Form1` directly into `config/printers_config.json`.

### Non-Goals
- Arbitrary per-slot `.lbx` file selection or dynamic field binding for custom `.lbx` object names (template mapping operates at the **Template Profile** level: `Molecule` vs. `Generic`).
- Changing the internal design/field names of the 6 `.lbx` files or enabling direct physical printing in `btnPrintLabel_Click_1` (the existing routing preview dialog stays intact).
- Moving config files to `%AppData%` (configs stay in `config/` relative to `AppDomain.CurrentDomain.BaseDirectory`).
- Installing external prerequisites (`pybis`, Brother b-PAC SDK, or printer drivers).

### Anti-Goals
- Never commit `config/` or real openBIS URLs/tokens to git.
- Never enter an unusable main window if required initial setup (`openbis_config.json` or `openbis_types.json`) was canceled before saving.
- Never silently overwrite user configs inside `bin/.../config/` during normal incremental builds (`F5`), and never fail fatally if Brother b-PAC fails while rendering example previews in the setup dialog.

---

## 3. Constraints

- **Platform & Stack:** Windows x64, .NET 10 WinForms (`net10.0-windows`, `<PlatformTarget>x64</PlatformTarget>`), `Newtonsoft.Json` 13.0.4, Brother b-PAC3 SDK COM component (`bpac`, `WrapperTool=tlbimp`), and external Python 3.9+ with `pybis` invoked via `python`.
- **BaseDirectory vs. Source Directory:** At runtime, C# resolves `config/` and `templates/` under `AppDomain.CurrentDomain.BaseDirectory` (so published builds like `FolderProfile.pubxml` and `dotnet run` / VS `F5` behave identically). In `OpenBISLabelStudio.csproj`, `templates\**\*.lbx` are copied with `PreserveNewest`; if a developer has a local untracked `OpenBISLabelStudio/config/` folder, `.csproj` may conditionally copy it with `PreserveNewest` (`Condition="Exists('config')"`), while `.gitignore` excludes `config/` and `**/config/`.
- **Keep `openbis_config.example.json`:** Keep `OpenBISLabelStudio/openbis_config.example.json` (tracked in git) and update `README.md` to document the new `config/` and `templates/` folders and first-run setup.

---

## 4. Approaches Considered

### Approach A — Unified Setup/Settings Dialog (`SetupSettingsForm`) + Extracted Config Service *(Selected)*
- Extract file paths, config DTOs, and JSON load/save into a dedicated service (`AppConfigService`), and build a single modal WinForms dialog (`SetupSettingsForm`) supporting two modes:
  - **Wizard mode** (first run / missing configs): guided **Back / Next / Finish** flow covering only what is needed (`Connection` -> `Object Types & Templates` -> `Printer Routing`), persisting each step's valid output so partial progress isn't lost if a later step is reopened.
  - **Settings mode** (from the `Settings` button on `Form1`, or jumped directly to `Connection` when a token expires): lets the user freely switch among the 3 sections (`Connection`, `Object Types & Templates`, `Printer Routing`) with existing values pre-populated.
- *Why selected:* Eliminates duplicated UI between first-run onboarding and Settings editing, shrinks `Form1.cs` (currently 1,134 lines), and keeps expired-token recovery identical everywhere.

### Approach B — Separate Single-Purpose Dialogs or Panels Embedded in `Form1` *(Rejected)*
- Three separate modal dialogs make **Back** navigation during onboarding awkward and make Settings editing fragmented. Embedding wizard panels inside `Form1` adds clutter to an already large form.

### Template Mapping Model *(Selected earlier)*
- **Template Profile per type (`Molecule` vs. `Generic`):** Each enabled openBIS type maps to either `Molecule` (uses `template_molecule_box_12mm.lbx`, `template_molecule_magnet_12mm.lbx`, `template_molecule_flask_24mm.lbx` and populates `studiedBy`, `evaporationTemperature`, hazard icons, `storageLocation`, `supplierOwnName`, `elnPreview`) or `Generic` (uses `template_small_box_12mm.lbx`, `template_magnet_12mm.lbx`, `template_flask_24mm.lbx` and enables `freeText`).
- *Why selected:* Matches `PopulateDocument()` in `Form1.cs` and preserves the 3-slot main window layout.

---

## 5. Detailed Design

### 5.1 Repository & Build Layout
- **`OpenBISLabelStudio/templates/`** (tracked in git):
  - `template_small_box_12mm.lbx`
  - `template_magnet_12mm.lbx`
  - `template_flask_24mm.lbx`
  - `template_molecule_box_12mm.lbx`
  - `template_molecule_magnet_12mm.lbx`
  - `template_molecule_flask_24mm.lbx`
- **`OpenBISLabelStudio/config/`** (untracked, created at runtime under `BaseDirectory\config\`):
  - `openbis_config.json`
  - `openbis_types.json`
  - `printers_config.json`
- **No legacy root-level migration:** `AppConfigService` reads and writes exclusively under `BaseDirectory\config\`. Any pre-existing root-level `openbis_config.json`, `openbis_types.json`, or `printers_config.json` in `BaseDirectory` (such as stale files in `bin\Debug\net10.0-windows\`) are ignored so missing `config\` files trigger the first-run wizard cleanly.
- **`.gitignore`:** Add `config/` and `**/config/` alongside the existing `openbis_config.json` and `openbis_results.json` rules.
- **Git cleanup:** Untrack/remove `OpenBISLabelStudio/openbis_types.json` and `OpenBISLabelStudio/printers_config.json` from the repo root.
- **`OpenBISLabelStudio.csproj`:** Update `<None>` items so `templates\**\*.lbx` and `openbis_fetch.py` copy to output (`PreserveNewest`), remove references to root `.lbx` / `openbis_types.json` / `printers_config.json` / `openbis_config.json`, and conditionally copy `config\**\*` if present locally (`Condition="Exists('config')"`).

### 5.2 Configuration Schemas (`AppConfigService`)

1. **`config/openbis_config.json`**
   ```json
   {
     "openbisUrl": "https://openbis.your-institution.ch",
     "token": "PERSONAL_ACCESS_TOKEN"
   }
   ```

2. **`config/openbis_types.json`**
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
   - `templateProfile` is serialized as `"Molecule"` or `"Generic"` (`LabelTemplateProfile` enum).
   - `displayName` caches the friendly description from openBIS so the type dropdown still displays readable names even if a transient network glitch occurs on startup.
   - **Backward-compatibility read:** If a hand-authored file contains a legacy `string[]` (e.g. `["SUBSTANCE", "CRYSTAL"]`), deserialize gracefully by mapping `"SUBSTANCE"` (case-insensitive) to `Molecule` and others to `Generic`. Always write the object-array schema on save.

3. **`config/printers_config.json`**
   ```json
   {
     "smallBoxPrinter": "Brother PT-P700",
     "magnetPrinter": "Brother PT-P700 (Kopie 1)",
     "flaskPrinter": "Brother PT-P700 (Kopie 2)"
   }
   ```

### 5.3 Python Script Changes (`openbis_fetch.py`)
1. **Unique temp output path:** Support an optional `--out <filepath>` flag (passed by C# as a unique path in `Path.GetTempPath()`, e.g., `openbis_results_<guid>.json`, which C# reads and deletes in `finally`). Fall back to `RESULTS_FILE` when `--out` is omitted so CLI testing still works.
2. **Token & session validation:**
   - In `create_openbis_connection(openbis_url, token)`: call `o = Openbis(openbis_url, verify_certificates=False)`, pass `save_token=False` to `o.set_token(token, save_token=False)` (with fallback to `o.set_token(token)` if an older `pybis` version does not accept `save_token`), and check `if not o.is_session_active(): raise InvalidTokenError("Session token is expired or invalid.")`.
3. **Structured error classification (`errorType`):**
   - Return `"errorType"` alongside `"success": false` and `"error": "<message>"`:
     - `"token_expired"`: `is_session_active()` is false, or exception message/type indicates authentication/session/token rejection (e.g., `"session"`, `"token"`, `"not valid"`, `"expired"`, `"unauthorized"`, `"401"`, `"403"`, `"authentication"`).
     - `"connection_error"`: request/socket/DNS/SSL/timeout/HTTP connection failures.
     - `"unknown_error"`: any other failure.
   - In C#, also capture `StandardError` from the Python process. If the output JSON file is missing or Python fails to launch (e.g., missing `python` or `ModuleNotFoundError: No module named 'pybis'`), surface the stderr detail clearly rather than a generic `"results file not created"` error.
4. **Sample display name logic unchanged:** Keep the `empa_number` + `batch` concatenation rule in `fetch_samples_by_type` and `fetch_sample_details` strictly tied to `sample_type == "SUBSTANCE"` (independent of which `LabelTemplateProfile` is mapped in `config/openbis_types.json`).

### 5.4 Unified Dialog (`SetupSettingsForm`) Behavior

#### Modes & Entry Points
- **Wizard Mode (`SetupWizard`):** Invoked on startup if any of the 3 config files is missing or invalid/incomplete (`openbis_config.json` missing or blank URL/token, `openbis_types.json` missing/empty, or `printers_config.json` missing). Opens at the earliest missing step:
  - Step 1: **openBIS Connection** (shown if `openbis_config.json` is missing/incomplete, or if saved token fails validation)
  - Step 2: **Object Types & Label Templates** (shown if `openbis_types.json` is missing/empty)
  - Step 3: **Printer Routing** (shown if `printers_config.json` is missing)
  - Note: Once Step 1 succeeds, `config/openbis_config.json` is saved immediately (so the user won't have to re-enter valid credentials if they adjust later steps). `config/openbis_types.json` and `config/printers_config.json` are saved when finishing the wizard (or per step once validated). If the user clicks **Cancel** while required configs (`openbis_config.json` or `openbis_types.json` or `printers_config.json` during first run) are still missing, prompt/exit cleanly (`DialogResult.Cancel` -> close main form) rather than leaving an unconfigured app open.
- **Expired-Token Recovery Mode:** Invoked when any startup or runtime openBIS call returns `errorType == "token_expired"`. Opens `SetupSettingsForm` directly to the **Connection** page with the saved URL pre-filled, the token field cleared/focused, and an inline warning: `"Your openBIS personal access token has expired or is invalid. Please enter a new token."` Saving a valid new token (live-validated against openBIS) updates `config/openbis_config.json` and lets the main window continue/retry.
- **Settings Mode (`Settings` button on `Form1`):** *(Design call)* Opens `SetupSettingsForm` with section tabs/navigation (`Connection`, `Object Types & Templates`, `Printer Routing`) all accessible and pre-loaded with current saved config values. **Test Connection** runs a live openBIS check in memory (caching fetched types for the Types tab) without writing to disk; **Save** validates local constraints (non-empty URL/token, at least one selected object type) and persists all three config files atomically without blocking on network access (supporting offline edits), while **Cancel** discards all unapplied edits.

#### Page 1 — Connection
- Controls: `Instance URL` textbox, `Personal Access Token` textbox (masked with `UseSystemPasswordChar = true` plus a `Show token` checkbox), status label, and `Connect` / `Test Connection` button.
- Validates non-empty URL (`http://` or `https://`) and non-empty token.
- Calls `openbis_fetch.py --types` asynchronously.
- On `token_expired`: displays inline error asking for a new token and keeps URL populated.
- On `connection_error` / other error: displays inline connection error detail (e.g., unreachable host or missing `pybis`).
- On success: caches the returned list of server `OpenBisTypeInfoDto` items (so Page 2 doesn't need a redundant query) and enables advancing to Page 2 (Wizard mode) or shows `"Connected successfully"` (Settings mode).

#### Page 2 — Object Types & Label Templates
- **Type & Mapping list (`DataGridView` or checked list + combo column):**
  - Filter/search box at top to filter large openBIS type lists by code or description.
  - Columns: `Use` (CheckBox), `Object Type` (`DisplayDescription (CODE)`), `Template Style` (ComboBox: `"Molecule / Substance"` or `"Generic / Free-text"`).
  - Pre-checks types already in `config/openbis_types.json` with their saved `templateProfile`. For newly seen types (including initial setup), default `SUBSTANCE` -> `Molecule / Substance` and all others -> `Generic / Free-text`.
  - Validation: require at least one enabled object type before continuing/saving.
- **Visual Example Previews:**
  - Displays a preview area showing the 3 label slots (`Small Box 12mm`, `Magnet 12mm`, `Flask 24mm`) for whichever template style is currently active (updates automatically when selecting a row or changing a row's `Template Style`, plus two quick toggle buttons `Preview: Molecule / Substance` and `Preview: Generic / Free-text` so the user can compare both side-by-side/sequentially anytime).
  - Previews are rendered directly from `templates/*.lbx` via Brother b-PAC (`Document.Open` -> populate realistic example data -> `Document.Export` to PNG):
    - **Molecule / Substance example:** Name `"CuPc-F16"`, PermID `"20261113-EX01"`, Studied By `"jdoe"` / `"Jane Doe"`, Evaporation Temp `"520 K"`, Storage `"Cabinet A-3"`, Supplier `"Sigma-Aldrich"`, sample hazard icons (`poisonous`, `dry`) shown and remaining icons hidden with the transparent 1x1 PNG.
    - **Generic / Free-text example:** Name `"Sample-001"`, PermID `"20261113-EX02"`, Free Text `"Batch #4 / Substrate"`.
  - **Graceful fallback:** If b-PAC COM fails (e.g., SDK missing on a dev machine) or a template file is absent, display clear descriptive text inside the preview slots instead of throwing.
  - If opened in **Settings mode** and the user switches directly to Page 2 without visiting Page 1 first, automatically fetch server types using the saved credentials (showing a loading indicator, and redirecting to Page 1 if the token is expired).

#### Page 3 — Printer Routing
- Controls: 3 ComboBoxes (`Small Box 12mm`, `Magnet 12mm`, `Flask 24mm`) populated with `PrinterSettings.InstalledPrinters`.
- Pre-selects matching entries from existing `config/printers_config.json`, or falls back via `SelectBestPrinterMatch` against `"Brother PT-P700"`, `"Brother PT-P700 (Kopie 1)"`, `"Brother PT-P700 (Kopie 2)"` (or first available system printer).
- If Windows reports zero installed printers, shows a clear note and keeps the default/configured string values so setup can still finish.

### 5.5 Main Form (`Form1.cs`) Updates
- **Startup orchestration:**
  1. Ensure `config/` directory exists only when saving (or check file existence first).
  2. Run `SetupSettingsForm` in Wizard mode if `openbis_config.json`, `openbis_types.json`, or `printers_config.json` is missing/incomplete. If canceled while required setup is incomplete, exit cleanly.
  3. Populate printers from `config/printers_config.json` (with `_isPopulatingPrinters` guard).
  4. Populate `openbisTypeComboBox` from `config/openbis_types.json`, refreshing descriptions from openBIS `--types` in the background/async:
     - If `--types` returns `token_expired`, trigger the Expired-Token Recovery flow and retry `--types` once updated.
     - If `--types` has a transient `connection_error`, warn non-fatally and keep cached `displayName` entries from `config/openbis_types.json`.
- **Template profile resolution:**
  - Replace `IsSubstance()` with `GetActiveTemplateProfile()`: reads `TemplateProfile` from the selected `OpenBisTypeItem` (falling back to looking up `_currentSampleDetails.SampleType` in configured types, or `LabelTemplateProfile.Generic`).
  - `GetTemplateFilePath(LabelTemplateType slot, LabelTemplateProfile profile)` resolves from `Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "templates", fileName)`.
  - `PopulateDocument` and `UpdateFreeTextControlsState` check `profile == LabelTemplateProfile.Molecule` instead of `typeCode == "SUBSTANCE"`.
- **Settings button & auto-saving printer dropdowns:**
  - Add a **Settings** button to `Form1` (layout adjusted cleanly without overlapping existing controls). Clicking it opens `SetupSettingsForm` in Settings mode; on `DialogResult.OK`, reload configs, refresh `openbisTypeComboBox` and printer dropdowns, and reset current previews if the active type changed or was removed.
  - Subscribe `SelectedIndexChanged` on `smallBoxPrinterComboBox`, `magnetPrinterComboBox`, and `flaskPrinterComboBox` (ignoring events when `_isPopulatingPrinters` is true) to update `_printerConfig` and immediately persist `config/printers_config.json`.
- **Runtime expired-token handling:**
  - In `openbisTypeComboBox_SelectedIndexChanged` and `loadPrintTemplates_Click`, if `errorType == "token_expired"`, prompt via `SetupSettingsForm` (Expired-Token Recovery mode) and automatically retry the failed fetch if a new valid token is saved.

---

## 6. What's Left to the Builder

- Exact class/file organization under `OpenBISLabelStudio/` (e.g., `Services/AppConfigService.cs`, `Services/OpenBisClient.cs`, `Services/LabelPreviewRenderer.cs`, `SetupSettingsForm.cs` + `.Designer.cs`).
- Exact WinForms pixel coordinates, anchoring, and visual styling for `SetupSettingsForm` and the **Settings** button in `Form1.Designer.cs`.
- Shared helper extraction for `ProcessStartInfo` Python invocation in `OpenBisClient` to eliminate the current duplication across `QueryOpenBisTypes`, `QueryOpenBisByType`, and `QueryOpenBisSampleDetails`.
- Testing & verification plan (C# unit tests or manual/scripted verification of `openbis_fetch.py` error classification, config persistence, and WinForms build/startup).
