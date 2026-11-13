# First-Run Configuration, Directory Layout, and Template Mapping Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move all runtime configs into a git-ignored `config/` directory and `.lbx` files into `templates/`, add structured openBIS token/connection validation, and build a unified first-run Wizard / Expired-Token Recovery / Settings dialog with live rendered label examples and printer routing.

**Architecture:** Extract config persistence (`AppConfigService`), Python/openBIS process invocation (`OpenBisClient`), and Brother b-PAC preview rendering (`LabelPreviewRenderer`) out of `Form1.cs` into focused services under `OpenBISLabelStudio/`. A single modal WinForms dialog (`SetupSettingsForm`) serves first-run Wizard, Expired-Token Recovery, and main-window Settings modes, while `Form1` drives template selection from `config/openbis_types.json` (`Molecule` vs. `Generic` profile) and auto-saves main-screen printer changes.

**Tech Stack:** .NET 10 WinForms (`net10.0-windows`, `x64`), C#, `Newtonsoft.Json` 13.0.4, Brother b-PAC3 COM (`bpac`, `tlbimp`), MSBuild (`Visual Studio Community 2026`), Python 3.14 + `pybis` 7.x, `pytest`.

**Spec:** [docs/superpowers/specs/2026-11-13-first-run-config-and-templates-design.md](file:///c:/Users/dafa/Documents/git/openbis-label-studio/docs/superpowers/specs/2026-11-13-first-run-config-and-templates-design.md)

## Global Constraints

- Target Windows x64, `net10.0-windows`, `<PlatformTarget>x64</PlatformTarget>`; build with Visual Studio `MSBuild.exe` (not `dotnet build`, which fails `ResolveComReference` with `MSB4803`).
- Config files (`openbis_config.json`, `openbis_types.json`, `printers_config.json`) live under `AppDomain.CurrentDomain.BaseDirectory/config/` and are ignored in git (`config/`, `**/config/`).
- No legacy root-level migration: ignore pre-existing root-level `.json` files in `BaseDirectory`; missing `config/` files trigger first-run setup cleanly.
- All 6 `.lbx` templates live in `OpenBISLabelStudio/templates/` (tracked in git) and copy to `BaseDirectory/templates/` on build.
- Temporary openBIS query JSON is written to a unique temp file via `--out <tempPath>` in `Path.GetTempPath()` and deleted after reading.
- Template mapping uses `LabelTemplateProfile` (`Molecule` vs. `Generic`) per object type; `empa_number + batch` naming in `openbis_fetch.py` stays strictly tied to `sample_type == "SUBSTANCE"`.
- In Settings mode, **Test Connection** runs live validation in memory without saving; **Save** checks local constraints and writes all 3 config files atomically offline-safe. Wizard and Expired-Token Recovery require live openBIS validation before accepting Connection settings.
- Canceling Wizard mode while required `config/` files remain missing closes the application cleanly.

## Review Focus

1. Empty/whitespace token or stale `~/.pybis` cached token passed to `openbis_fetch.py` must immediately return `success: false` with `errorType: "token_expired"`, never silently reusing `~/.pybis`.
2. Corrupt or empty JSON inside `config/openbis_config.json`, `config/openbis_types.json`, or `config/printers_config.json` must not crash startup; treat as missing/incomplete so setup handles recovery.
3. Missing `python` executable or missing `pybis` (`ModuleNotFoundError`) preventing `--out` JSON creation must capture `StandardError` in `OpenBisClient` and surface a diagnostic error with `errorType: "unknown_error"`.
4. Zero installed Windows printers (`PrinterSettings.InstalledPrinters.Count == 0`) must not throw index errors in `SetupSettingsForm` or `Form1` and must preserve configured/default printer names.
5. Missing `.lbx` template or b-PAC COM exception during Setup example preview rendering must return a descriptive fallback preview bitmap instead of throwing or crashing the dialog.

---

## File Structure Map

| File | Responsibility |
| --- | --- |
| `.gitignore` | Ignore `config/` and `**/config/` everywhere |
| `OpenBISLabelStudio/templates/*.lbx` | Tracked Brother label template directory (6 files) |
| `OpenBISLabelStudio/OpenBISLabelStudio.csproj` | Copy `templates\**\*.lbx`, `openbis_fetch.py`, and optional local `config\**\*` |
| `OpenBISLabelStudio/openbis_fetch.py` | `--out` temp file support, `save_token=False` + `is_session_active()`, `errorType` classification |
| `tests/test_openbis_fetch.py` | `pytest` unit tests for `openbis_fetch.py` |
| `OpenBISLabelStudio/Models/ConfigModels.cs` | Config records, `LabelTemplateProfile` / `LabelTemplateType` enums, openBIS DTOs |
| `OpenBISLabelStudio/Services/AppConfigService.cs` | `config/` & `templates/` path resolution, JSON load/save (with legacy array fallback), wizard step detection, `SelectBestPrinterMatch` |
| `OpenBISLabelStudio/Services/OpenBisClient.cs` | Shared `python` runner using temp `--out` paths, stderr fallback, async query methods |
| `OpenBISLabelStudio/Services/LabelPreviewRenderer.cs` | b-PAC document population, PNG rendering, example dummy data, COM-safe fallback bitmaps |
| `OpenBISLabelStudio/SetupSettingsForm.cs` + `.Designer.cs` | Unified modal form for Wizard, Expired-Token Recovery, and Settings modes |
| `OpenBISLabelStudio/Form1.cs` + `.Designer.cs` | Startup onboarding hook, **Settings** button, profile-driven templates, printer auto-save, token recovery |
| `OpenBISLabelStudio.Tests/OpenBISLabelStudio.Tests.csproj` + `Program.cs` | Zero-external-NuGet C# test runner built by `MSBuild.exe` |
| `README.md` | Updated folder layout, `config/`/`templates/` docs, and first-run setup notes |

---

### Task 1: Restructure Repository Folders, Git Tracking, and Project File

**Files:**
- Modify: `.gitignore`
- Move: `OpenBISLabelStudio/template_*.lbx` -> `OpenBISLabelStudio/templates/template_*.lbx`
- Remove from git: `OpenBISLabelStudio/openbis_types.json`, `OpenBISLabelStudio/printers_config.json`
- Modify: `OpenBISLabelStudio/OpenBISLabelStudio.csproj`

**Interfaces:**
- Produces: Output directory contract `$(OutDir)templates\template_*.lbx` and `$(OutDir)openbis_fetch.py`.

- [ ] **Step 1: Update `.gitignore` to exclude `config/` directories**

Add `config/` and `**/config/` under the sensitive configuration section in `.gitignore` (keeping existing `openbis_config.json` and `openbis_results.json` ignores).

- [ ] **Step 2: Move `.lbx` templates into `OpenBISLabelStudio/templates/` and untrack old root configs**

Run:
```powershell
New-Item -ItemType Directory -Force OpenBISLabelStudio\templates | Out-Null
git mv OpenBISLabelStudio\template_small_box_12mm.lbx OpenBISLabelStudio\templates\
git mv OpenBISLabelStudio\template_magnet_12mm.lbx OpenBISLabelStudio\templates\
git mv OpenBISLabelStudio\template_flask_24mm.lbx OpenBISLabelStudio\templates\
git mv OpenBISLabelStudio\template_molecule_box_12mm.lbx OpenBISLabelStudio\templates\
git mv OpenBISLabelStudio\template_molecule_magnet_12mm.lbx OpenBISLabelStudio\templates\
git mv OpenBISLabelStudio\template_molecule_flask_24mm.lbx OpenBISLabelStudio\templates\
git rm OpenBISLabelStudio\openbis_types.json OpenBISLabelStudio\printers_config.json
```

- [ ] **Step 3: Update `OpenBISLabelStudio/OpenBISLabelStudio.csproj` asset copy rules**

Replace the `<None Update="...">` block in `OpenBISLabelStudio/OpenBISLabelStudio.csproj:28-59` so it copies `openbis_fetch.py`, `templates\**\*.lbx` (`PreserveNewest`), and conditionally copies `config\**\*` if present locally (`Condition="Exists('config')"`):

```xml
  <ItemGroup>
    <None Update="openbis_fetch.py">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
    <None Update="templates\**\*.lbx">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
    <None Update="config\**\*" Condition="Exists('config')">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>
```

- [ ] **Step 4: Clean stale `bin/` and verify MSBuild outputs `templates\*.lbx` without root `.lbx`/`.json` configs**

Run:
```powershell
Remove-Item -Recurse -Force OpenBISLabelStudio\bin, OpenBISLabelStudio\obj -ErrorAction SilentlyContinue
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" OpenBISLabelStudio.slnx /t:Build /p:Configuration=Debug /nologo
Get-ChildItem OpenBISLabelStudio\bin\Debug\net10.0-windows\templates\*.lbx | Select-Object -ExpandProperty Name
```
Expected: `Build succeeded. 0 Error(s)`, all 6 `template_*.lbx` files appear inside `bin\Debug\net10.0-windows\templates\`, and no `.json` config files are copied into `bin\Debug\net10.0-windows\`.

- [ ] **Step 5: Commit**

```powershell
git add .gitignore OpenBISLabelStudio/OpenBISLabelStudio.csproj OpenBISLabelStudio/templates
git commit -m "chore: move templates into templates/ and untrack config files"
```

---

### Task 2: Update `openbis_fetch.py` for `--out`, Session Validation, and Error Classification

**Files:**
- Modify: `OpenBISLabelStudio/openbis_fetch.py`
- Create: `tests/test_openbis_fetch.py`

**Interfaces:**
- Produces CLI contract: `python openbis_fetch.py <url> <token> (--types | <sample_type> | --details <perm_id>) [--out <json_path>]`
- Produces JSON envelope:
  - Success: `{"success": true, ...}`
  - Failure: `{"success": false, "errorType": "token_expired" | "connection_error" | "unknown_error", "error": "<message>"}`
- Produces Python functions in `openbis_fetch.py`:
  - `class InvalidTokenError(Exception)`
  - `create_openbis_connection(openbis_url: str, token: str) -> Openbis`
  - `classify_error(exc: Exception) -> str`
  - `parse_cli_args(argv: list[str]) -> tuple[str, str, str, str | None, str]` returning `(url, token, mode_or_type, perm_id, out_path)`

- [ ] **Step 1: Write the failing pytest tests in `tests/test_openbis_fetch.py`**

Create `tests/test_openbis_fetch.py` covering Review Focus #1 (empty token / invalid session must return `token_expired`, never use `~/.pybis` cache) plus `--out`, error classification, and `SUBSTANCE` naming:

```python
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


def test_inactive_session_raises_invalid_token_error():
    client = MagicMock()
    client.is_session_active.return_value = False
    with patch.object(openbis_fetch, "Openbis", return_value=client):
        with pytest.raises(openbis_fetch.InvalidTokenError):
            openbis_fetch.create_openbis_connection("https://openbis.example.org", "tok-123")
    client.set_token.assert_called_once_with("tok-123", save_token=False)


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


def test_cli_writes_to_out_path_instead_of_cwd(tmp_path, monkeypatch):
    out_file = tmp_path / "custom_out.json"
    monkeypatch.chdir(tmp_path)
    with patch.object(openbis_fetch, "fetch_sample_types", return_value={"success": True, "types": []}):
        exit_code = openbis_fetch.main(["openbis_fetch.py", "https://x", "tok", "--types", "--out", str(out_file)])
    assert exit_code == 0
    assert out_file.exists()
    assert not (tmp_path / openbis_fetch.RESULTS_FILE).exists()
    assert json.loads(out_file.read_text(encoding="utf-8")) == {"success": True, "types": []}
```

- [ ] **Step 2: Run pytest to verify the new tests fail**

Run: `python -m pytest tests/test_openbis_fetch.py -v`
Expected: FAIL (`AttributeError: module 'openbis_fetch' has no attribute 'InvalidTokenError'` etc.).

- [ ] **Step 3: Implement updates in `OpenBISLabelStudio/openbis_fetch.py`**

1. Remove unused `PRECURSORS_COLLECTION = "/LAB205_MATERIALS/MOLECULES/PRECURSOR_COLLECTION"`.
2. Add `InvalidTokenError(Exception)` and `classify_error(exc: Exception) -> str`:
   - `isinstance(exc, InvalidTokenError)` -> `"token_expired"`
   - `isinstance(exc, requests.exceptions.RequestException)` -> `"connection_error"`
   - `isinstance(exc, ValueError)`: if message contains `"url"`, `"hostname"`, or `"https"` -> `"connection_error"`; if message contains `"session"`, `"token"`, `"valid"`, `"expired"`, `"login"` -> `"token_expired"`
   - Any exception message containing `"401"`, `"403"`, `"unauthorized"`, `"authentication"`, `"invalid session"`, `"session expired"`, `"token"` -> `"token_expired"`; connection/timeout/ssl/name-resolution keywords -> `"connection_error"`; else `"unknown_error"`.
3. Update `create_openbis_connection(openbis_url, token)`:
   - Raise `InvalidTokenError("Personal access token is empty.")` if `not token or not str(token).strip()`.
   - Instantiate `o = Openbis(openbis_url, verify_certificates=False)`.
   - Call `o.set_token(token, save_token=False)` (catching `TypeError` for older `pybis` signatures and falling back to `o.set_token(token)`).
   - Check `if not o.is_session_active(): raise InvalidTokenError("Session token is expired or invalid.")`.
4. Include `"errorType": classify_error(e)` on all error dicts returned by `fetch_sample_types`, `fetch_samples_by_type`, and `fetch_sample_details` (preserving existing `sample_type == "SUBSTANCE"` check for `empa_number` + `batch`).
5. Extract `parse_cli_args(argv)` and `main(argv=None) -> int` supporting optional `--out <filepath>` anywhere after `mode_or_type` (defaulting to `RESULTS_FILE`), writing the JSON output to `out_path`, and invoke `sys.exit(main())` in `if __name__ == "__main__":`.

- [ ] **Step 4: Run pytest to verify all tests pass**

Run: `python -m pytest tests/test_openbis_fetch.py -v`
Expected: PASS (6 passed).

- [ ] **Step 5: Commit**

```powershell
git add OpenBISLabelStudio/openbis_fetch.py tests/test_openbis_fetch.py
git commit -m "feat: add --out path, session validation, and errorType to openbis_fetch.py"
```

---

### Task 3: Extract Models, `AppConfigService`, and Create C# Test Harness

**Files:**
- Create: `OpenBISLabelStudio/Models/ConfigModels.cs`
- Create: `OpenBISLabelStudio/Services/AppConfigService.cs`
- Create: `OpenBISLabelStudio.Tests/OpenBISLabelStudio.Tests.csproj`
- Create: `OpenBISLabelStudio.Tests/Program.cs`
- Modify: `OpenBISLabelStudio.slnx`

**Interfaces:**
- Produces in `OpenBISLabelStudio.Models`:
  - `enum LabelTemplateProfile { Generic, Molecule }`
  - `enum LabelTemplateType { SmallBox12mm, Magnet12mm, Flask24mm }`
  - `enum SetupWizardStep { None, Connection, ObjectTypes, Printers }`
  - `class AppConfig { string OpenBisUrl; string Token; }`
  - `class ConfiguredOpenBisType { string Code; string DisplayName; LabelTemplateProfile TemplateProfile; }`
  - `class PrinterConfig { string SmallBoxPrinter; string MagnetPrinter; string FlaskPrinter; }`
  - `class OpenBisTypeItem { string Code; string DisplayText; LabelTemplateProfile TemplateProfile; }`
  - `OpenBisTypesResult`, `OpenBisTypeInfoDto`, `OpenBisListResult`, `SampleDto`, `OpenBisDetailsResult`, `SampleDetailsDto`, `IconFlagsDto` (with `[JsonProperty("errorType")] string ErrorType` on all 3 result classes).
- Produces in `OpenBISLabelStudio.Services.AppConfigService`:
  - `AppConfigService(string? baseDirectory = null)`
  - `string ConfigDirectoryPath { get; }`
  - `string TemplatesDirectoryPath { get; }`
  - `string OpenBisConfigPath { get; }`
  - `string OpenBisTypesPath { get; }`
  - `string PrintersConfigPath { get; }`
  - `bool HasValidOpenBisConfig()`
  - `bool HasValidConfiguredTypes()`
  - `bool HasPrinterConfig()`
  - `SetupWizardStep GetFirstMissingWizardStep()`
  - `AppConfig? LoadAppConfig()`
  - `void SaveAppConfig(AppConfig config)`
  - `List<ConfiguredOpenBisType> LoadConfiguredTypes()`
  - `void SaveConfiguredTypes(IEnumerable<ConfiguredOpenBisType> types)`
  - `PrinterConfig? LoadPrinterConfig()`
  - `void SavePrinterConfig(PrinterConfig config)`
  - `static PrinterConfig CreateDefaultPrinterConfig()`
  - `string GetTemplateFilePath(LabelTemplateType slot, LabelTemplateProfile profile)`
  - `static int FindBestPrinterIndex(IReadOnlyList<string> availablePrinters, string preferredName)`

- [ ] **Step 1: Add `OpenBISLabelStudio.Tests` project skeleton to `OpenBISLabelStudio.slnx` and write failing config tests**

Create `OpenBISLabelStudio.Tests/OpenBISLabelStudio.Tests.csproj` (`net10.0-windows`, `OutputType=Exe`, `PlatformTarget=x64`, `UseWindowsForms=true`, `<ProjectReference Include="..\OpenBISLabelStudio\OpenBISLabelStudio.csproj" />`), register it in `OpenBISLabelStudio.slnx`, and add test cases in `OpenBISLabelStudio.Tests/Program.cs`:
1. `IgnoresLegacyRootJsonFiles`: place `openbis_config.json`, `openbis_types.json`, `printers_config.json` in temp root (outside `config/`), assert `GetFirstMissingWizardStep() == SetupWizardStep.Connection`, `LoadAppConfig() == null`, `LoadConfiguredTypes().Count == 0`, `LoadPrinterConfig() == null`.
2. `CorruptOrEmptyJsonDoesNotThrowAndTriggersSetup`: write `"{not-json"` to `config/openbis_config.json` and `"[]"` to `config/openbis_types.json` (Review Focus #2), assert `HasValidOpenBisConfig() == false`, `HasValidConfiguredTypes() == false`, `LoadAppConfig() == null`, `LoadConfiguredTypes().Count == 0`.
3. `SaveAndLoadAllConfigsRoundTrip`: save `AppConfig`, `ConfiguredOpenBisType[]`, and `PrinterConfig`, verify files exist inside `config/` and deserialize cleanly with `LabelTemplateProfile` string enum (`"Molecule"` / `"Generic"`).
4. `LegacyStringArrayTypesReadCompatibility`: write `["SUBSTANCE", "CRYSTAL"]` inside `config/openbis_types.json`, assert `LoadConfiguredTypes()` maps `"SUBSTANCE"` -> `LabelTemplateProfile.Molecule` and `"CRYSTAL"` -> `LabelTemplateProfile.Generic`.
5. `ResolvesTemplatePathsUnderTemplatesFolder`: assert `GetTemplateFilePath(LabelTemplateType.Magnet12mm, LabelTemplateProfile.Molecule)` ends with `templates\template_molecule_magnet_12mm.lbx` and `Generic` ends with `templates\template_magnet_12mm.lbx`.
6. `FindBestPrinterIndexHandlesZeroPrinters`: assert `FindBestPrinterIndex(Array.Empty<string>(), "Brother PT-P700") == -1` (Review Focus #4).

- [ ] **Step 2: Run MSBuild on `OpenBISLabelStudio.slnx` to verify tests fail to compile**

Run: `& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" OpenBISLabelStudio.slnx /t:Build /p:Configuration=Debug /nologo`
Expected: FAIL with missing types `AppConfigService`, `SetupWizardStep`, `LabelTemplateProfile`, etc.

- [ ] **Step 3: Implement `OpenBISLabelStudio/Models/ConfigModels.cs` and `OpenBISLabelStudio/Services/AppConfigService.cs`**

- In `ConfigModels.cs`: define all enums, config models (`TemplateProfile` using `[JsonConverter(typeof(StringEnumConverter))]`), and DTOs (removing duplicate DTO classes from `Form1.cs` later in Task 6, or wait — to keep `Form1.cs` compiling right now before Task 6 edits `Form1.cs`, place new models/services in `OpenBISLabelStudio.Models` / `OpenBISLabelStudio.Services` namespaces while leaving `Form1.cs`'s existing `OpenBISLabelStudio`-namespace DTOs untouched until Task 6!).
  *(Note: keeping new models in `OpenBISLabelStudio.Models` prevents any name collision with `Form1.cs` until Task 6 cleans up `Form1.cs`.)*
- In `AppConfigService.cs`:
  - Read strictly from `Path.Combine(_baseDirectory, "config")`, never from root `_baseDirectory`.
  - Wrap JSON reads in `try/catch` returning `null`/empty list on corrupt JSON.
  - Write files atomically via `Directory.CreateDirectory(ConfigDirectoryPath)` + write temp + `File.Move(..., overwrite: true)` (or `File.WriteAllText`).
  - `GetFirstMissingWizardStep()`: return `Connection` if `!HasValidOpenBisConfig()`, else `ObjectTypes` if `!HasValidConfiguredTypes()`, else `Printers` if `!HasPrinterConfig()`, else `None`.
  - Support legacy `string[]` deserialization inside `LoadConfiguredTypes()` when JSON starts with `[` of strings (`JToken` inspection).

- [ ] **Step 4: Build and run `OpenBISLabelStudio.Tests.exe`**

Run:
```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" OpenBISLabelStudio.slnx /t:Build /p:Configuration=Debug /nologo
& "OpenBISLabelStudio.Tests\bin\Debug\net10.0-windows\OpenBISLabelStudio.Tests.exe"
```
Expected: `Build succeeded. 0 Error(s)` and all 6 C# tests pass (`exit code 0`).

- [ ] **Step 5: Commit**

```powershell
git add OpenBISLabelStudio/Models OpenBISLabelStudio/Services OpenBISLabelStudio.Tests OpenBISLabelStudio.slnx
git commit -m "feat: add ConfigModels, AppConfigService, and C# unit test runner"
```

---

### Task 4: Implement `OpenBisClient` and `LabelPreviewRenderer` Services

**Files:**
- Create: `OpenBISLabelStudio/Services/OpenBisClient.cs`
- Create: `OpenBISLabelStudio/Services/LabelPreviewRenderer.cs`
- Modify: `OpenBISLabelStudio.Tests/Program.cs`

**Interfaces:**
- Consumes: `OpenBISLabelStudio.Models.*`, `AppConfigService`, `openbis_fetch.py --out` CLI contract.
- Produces in `OpenBISLabelStudio.Services.OpenBisClient`:
  - `OpenBisClient(string? baseDirectory = null, string? pythonExecutable = null)`
  - `Task<OpenBisTypesResult> QueryTypesAsync(string url, string token)`
  - `Task<OpenBisListResult> QuerySamplesByTypeAsync(string url, string token, string sampleType)`
  - `Task<OpenBisDetailsResult> QuerySampleDetailsAsync(string url, string token, string permId)`
  - `public static T DeserializeOrError<T>(string? jsonContent, int exitCode, string stdErr, Func<string, string, T> errorFactory)`
- Produces in `OpenBISLabelStudio.Services.LabelPreviewRenderer`:
  - `LabelPreviewRenderer(AppConfigService configService)`
  - `Image RenderTemplatePreview(LabelTemplateType slot, LabelTemplateProfile profile, SampleDetailsDto details, string freeText = "")` (returns rendered label `Bitmap`, or `CreateFallbackPreviewBitmap` when missing/failed)
  - `Dictionary<LabelTemplateType, Image> RenderExamplePreviews(LabelTemplateProfile profile)`
  - `static SampleDetailsDto BuildExampleSampleDetails(LabelTemplateProfile profile)`
  - `static Bitmap CreateFallbackPreviewBitmap(LabelTemplateType slot, LabelTemplateProfile profile, string reason)`
  - `string GetOrCreateTransparentPng()`

- [ ] **Step 1: Add failing tests for `OpenBisClient` and `LabelPreviewRenderer` in `OpenBISLabelStudio.Tests/Program.cs`**

Add tests pinning Review Focus #3 and #5:
1. `OpenBisClientSurfacesStderrWhenPythonFails`: invoke `OpenBisClient` with a nonexistent `pythonExecutable` (e.g., `"nonexistent-python-binary-xyz"`) or mock stderr via `DeserializeOrError(null, 1, "ModuleNotFoundError: No module named 'pybis'", ...)` and assert `Success == false`, `ErrorType == "unknown_error"`, and `Error` contains `"pybis"` / failure message.
2. `OpenBisClientExecutesScriptAndCleansTempOutput`: point a test `OpenBisClient` at a tiny temp directory containing a stub `openbis_fetch.py` that writes `--out` JSON (`{"success": false, "errorType": "token_expired", "error": "Expired"}`), assert `QueryTypesAsync` returns `ErrorType == "token_expired"` and leaves zero temp JSON files behind in `Path.GetTempPath()` (or verifies the script's `--out` path is deleted).
3. `BuildExampleSampleDetailsMatchesProfile`: verify `Molecule` returns `"CuPc-F16"` / `"20261113-EX01"` / `"520 K"` / hazard flags (`Poisonous = true`, `Dry = true`) and `Generic` returns `"Sample-001"` / `"20261113-EX02"`.
4. `RenderTemplatePreviewReturnsFallbackBitmapWhenTemplateMissing`: use an `AppConfigService` pointing to an empty temp directory (no `.lbx` files), call `RenderExamplePreviews(LabelTemplateProfile.Molecule)`, assert all 3 slots return non-null `Bitmap` images instead of throwing.

- [ ] **Step 2: Build and run `OpenBISLabelStudio.Tests.exe` to verify new tests fail**

Run:
```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" OpenBISLabelStudio.slnx /t:Build /p:Configuration=Debug /nologo
```
Expected: FAIL with missing `OpenBisClient` and `LabelPreviewRenderer`.

- [ ] **Step 3: Implement `OpenBisClient.cs` and `LabelPreviewRenderer.cs`**

- **`OpenBisClient.cs`**:
  - Single private `RunScriptAsync<T>(string[] args, Func<string, string, T> errorFactory)` helper:
    - Generates unique `tempOut = Path.Combine(Path.GetTempPath(), $"openbis_results_{Guid.NewGuid():N}.json")`.
    - Escapes/quotes script path, args, `"--out"`, `tempOut` cleanly (`ProcessStartInfo.ArgumentList.Add(...)` avoids command-line quoting bugs with special characters in tokens or URLs!).
    - Reads `StandardOutput` & `StandardError` asynchronously before `WaitForExitAsync()`, then in `finally` deletes `tempOut`.
    - If `tempOut` is missing or empty, uses `stdErr` if non-empty (falling back to launch exception message) with `errorType: "unknown_error"`.
- **`LabelPreviewRenderer.cs`**:
  - Move `PopulateDocument`, `SetIconVisibility`, and `GetOrCreateTransparentPng` logic from `Form1.cs` here, keyed by `LabelTemplateProfile` instead of `bool isSubstance` (`profile == LabelTemplateProfile.Molecule`).
  - `RenderTemplatePreview` catches missing files or `bpac` COM exceptions and returns `CreateFallbackPreviewBitmap(slot, profile, reason)` (drawing slot title, profile, and reason clearly on a white bordered bitmap) rather than returning null or throwing.

- [ ] **Step 4: Build and run `OpenBISLabelStudio.Tests.exe` to verify all tests pass**

Run:
```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" OpenBISLabelStudio.slnx /t:Build /p:Configuration=Debug /nologo
& "OpenBISLabelStudio.Tests\bin\Debug\net10.0-windows\OpenBISLabelStudio.Tests.exe"
```
Expected: PASS (10/10 C# tests passing).

- [ ] **Step 5: Commit**

```powershell
git add OpenBISLabelStudio/Services/OpenBisClient.cs OpenBISLabelStudio/Services/LabelPreviewRenderer.cs OpenBISLabelStudio.Tests/Program.cs
git commit -m "feat: add OpenBisClient and LabelPreviewRenderer services"
```

---

### Task 5: Build `SetupSettingsForm` (Wizard, Expired-Token Recovery, and Settings Modes)

**Files:**
- Create: `OpenBISLabelStudio/SetupSettingsForm.cs`
- Create: `OpenBISLabelStudio/SetupSettingsForm.Designer.cs`
- Modify: `OpenBISLabelStudio.Tests/Program.cs`

**Interfaces:**
- Consumes: `AppConfigService`, `OpenBisClient`, `LabelPreviewRenderer`, `OpenBISLabelStudio.Models.*`.
- Produces in `OpenBISLabelStudio`:
  - `enum SetupDialogMode { Wizard, ExpiredTokenRecovery, Settings }`
  - `class SetupSettingsForm : Form`
    - Constructor: `SetupSettingsForm(AppConfigService configService, OpenBisClient openBisClient, LabelPreviewRenderer previewRenderer, SetupDialogMode mode, SetupWizardStep startStep = SetupWizardStep.Connection, string? initialWarning = null, Func<IReadOnlyList<string>>? installedPrintersProvider = null)`
    - Public helper methods/properties for deterministic testing and Form1 integration:
      - `SetupDialogMode Mode { get; }`
      - `static List<ConfiguredOpenBisType> BuildInitialTypeRows(IEnumerable<OpenBisTypeInfoDto> serverTypes, IReadOnlyList<ConfiguredOpenBisType> existingConfigured)`
      - `bool TrySaveSettingsOffline(out string validationError)` (used by Settings Save button)
      - `IReadOnlyList<OpenBisTypeInfoDto>? CachedServerTypes { get; }`

- [ ] **Step 1: Write failing tests for `SetupSettingsForm` behavior in `OpenBISLabelStudio.Tests/Program.cs`**

Add tests covering all modes and Review Focus #4:
1. `BuildInitialTypeRowsDefaultsSubstanceToMoleculeAndPreservesSaved`: verify `SUBSTANCE` defaults to `Molecule` when no saved config exists, others default to `Generic`, and existing saved mappings override defaults and stay checked.
2. `SetupSettingsFormInitializesSafelyWithZeroPrinters`: construct `SetupSettingsForm` in `Wizard` (`StartStep = Printers`) and `Settings` modes passing `installedPrintersProvider: () => Array.Empty<string>()` (Review Focus #4), assert no exception is thrown and printer selections fall back cleanly to configured/default names.
3. `SetupSettingsFormExpiredTokenModePreFillsUrlAndClearsToken`: save an `AppConfig`, instantiate `SetupSettingsForm` in `ExpiredTokenRecovery` mode with `"Token expired"`, assert URL is pre-filled and token input is empty/focused for replacement.
4. `SetupSettingsFormSettingsSavePersistsOfflineAndCancelDiscards`: open in `Settings` mode with saved configs, update printer/type state, verify `Cancel` leaves disk files unchanged while `TrySaveSettingsOffline` updates all 3 `config/*.json` files atomically without calling network.

- [ ] **Step 2: Run MSBuild to verify compilation fails as expected**

Run: `& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" OpenBISLabelStudio.slnx /t:Build /p:Configuration=Debug /nologo`
Expected: FAIL (`SetupSettingsForm` / `SetupDialogMode` not found).

- [ ] **Step 3: Implement `SetupSettingsForm.Designer.cs` and `SetupSettingsForm.cs`**

Design and implement the 3-page WinForms dialog (sized cleanly, e.g., `940 x 680`, fixed dialog or resizable with anchors):
- **Navigation bar:**
  - In `Wizard` mode: step header + bottom `Back`, `Next` / `Finish`, `Cancel`. Only steps whose config files are missing (plus any step the user explicitly steps forward into from Step 1 if incomplete) are required: if `HasValidConfiguredTypes()` and `HasPrinterConfig()` are already true when Step 1 completes, Step 1 finishes the wizard immediately (`DialogResult.OK`); otherwise it advances to the next missing step (`ObjectTypes` if `!HasValidConfiguredTypes()`, else `Printers`), and `Next` on Step 1 runs live `OpenBisClient.QueryTypesAsync`, shows `"token_expired"` or `"connection_error"` inline, saves `openbis_config.json` on success, and caches `CachedServerTypes`.
  - In `ExpiredTokenRecovery` mode: shows only Connection page with saved URL pre-filled, empty token textbox focused, inline expiry warning banner, `Save Token` (runs live `QueryTypesAsync` validation; only closes with `DialogResult.OK` and saves `openbis_config.json` when valid), and `Cancel`.
  - In `Settings` mode: shows top navigation tabs/buttons (`1. Connection`, `2. Object Types & Templates`, `3. Printer Routing`) plus bottom `Save` and `Cancel`.
- **Page 1 (Connection):** `Instance URL` textbox, `Personal Access Token` masked textbox + `Show token` checkbox, `Test Connection` button (in Settings mode tests live without disk write; in Wizard/Recovery validates & saves token), inline status label.
- **Page 2 (Object Types & Label Templates):**
  - Search/filter `TextBox` filtering visible rows by code or friendly description without losing checked/style state of hidden rows.
  - `DataGridView` (`Use` CheckBoxColumn, `Type` read-only TextBoxColumn showing `Description (CODE)`, `Template Style` ComboBoxColumn with `"Molecule / Substance"` and `"Generic / Free-text"`).
  - Live Example Previews panel below the grid: toggle buttons `Preview: Molecule / Substance` and `Preview: Generic / Free-text`, plus 3 `PictureBox`es (`Small Box 12mm`, `Magnet 12mm`, `Flask 24mm`) populated via `LabelPreviewRenderer.RenderExamplePreviews(...)` (cached per profile inside the form for crisp switching) whenever a row is selected, its `Template Style` changes, or a preview toggle button is clicked. Dispose rendered images in `FormClosed`/`Dispose`.
  - In Settings mode, entering Page 2 when `CachedServerTypes == null` automatically calls `QueryTypesAsync` using the current URL/token controls (showing loading indicator; if `token_expired`, switches to Page 1 with inline warning; if offline/connection error, populates grid from existing `config/openbis_types.json` with a clear offline notice so offline edits still work!).
- **Page 3 (Printer Routing):**
  - 3 labeled `ComboBox`es (`Small Box (12mm)`, `Magnet (12mm)`, `Flask (24mm)`) populated from `installedPrintersProvider?.Invoke() ?? PrinterSettings.InstalledPrinters`.
  - When zero printers exist, show warning label `"No Windows printers detected — saved/default printer names will be kept."` and keep non-crashing text/items.

- [ ] **Step 4: Build and run `OpenBISLabelStudio.Tests.exe`**

Run:
```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" OpenBISLabelStudio.slnx /t:Build /p:Configuration=Debug /nologo
& "OpenBISLabelStudio.Tests\bin\Debug\net10.0-windows\OpenBISLabelStudio.Tests.exe"
```
Expected: PASS (all 14 C# tests passing).

- [ ] **Step 5: Commit**

```powershell
git add OpenBISLabelStudio/SetupSettingsForm.cs OpenBISLabelStudio/SetupSettingsForm.Designer.cs OpenBISLabelStudio.Tests/Program.cs
git commit -m "feat: add unified SetupSettingsForm for wizard, token recovery, and settings"
```

---

### Task 6: Integrate Services & Setup Flow into `Form1`, Add Settings Button, and Update `README.md`

**Files:**
- Modify: `OpenBISLabelStudio/Form1.Designer.cs`
- Modify: `OpenBISLabelStudio/Form1.cs`
- Modify: `OpenBISLabelStudio.Tests/Program.cs`
- Modify: `README.md`

**Interfaces:**
- Consumes: `AppConfigService`, `OpenBisClient`, `LabelPreviewRenderer`, `SetupSettingsForm`, `OpenBISLabelStudio.Models.*`.
- Preserves public form behavior while removing duplicate config/process/b-PAC/DTO code from `Form1.cs`.

- [ ] **Step 1: Add test in `OpenBISLabelStudio.Tests/Program.cs` for `Form1` profile resolution / instantiation safety**

Add a test verifying `Form1.ResolveTemplateProfile(selectedTypeCode, configuredTypes, sampleDetailsType)` returns `Molecule` when mapped in `config/openbis_types.json` even if `selectedTypeCode != "SUBSTANCE"`, and `Generic` otherwise.

- [ ] **Step 2: Run MSBuild to confirm test fails before `Form1` update**

Run: `& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" OpenBISLabelStudio.slnx /t:Build /p:Configuration=Debug /nologo`
Expected: FAIL (`Form1.ResolveTemplateProfile` does not exist).

- [ ] **Step 3: Update `Form1.Designer.cs` and `Form1.cs`**

1. **`Form1.Designer.cs`**: Add `btnSettings` (`"Settings..."`) cleanly aligned in the bottom button row alongside `loadPrintTemplates` and `btnPrintLabel` (adjusting spacing symmetrically within `ClientSize = 1080 x 530` without overlaps).
2. **`Form1.cs`**:
   - Remove duplicated DTO classes, raw `ProcessStartInfo` runners, and raw b-PAC `PopulateDocument` / `RenderPreview` logic (delegating to `AppConfigService`, `OpenBisClient`, `LabelPreviewRenderer`).
   - Add `_isPopulatingPrinters` guard around `PopulateInstalledPrinters()`, and wire `SelectedIndexChanged` on all 3 printer ComboBoxes so user changes immediately update `_printerConfig` and call `_configService.SavePrinterConfig(_printerConfig)`.
   - On `Form1_Load`:
     - Check `_configService.GetFirstMissingWizardStep()`. If not `None`, open `SetupSettingsForm` in `SetupDialogMode.Wizard`; if `DialogResult != DialogResult.OK` (and required configs still missing), close `Form1` cleanly (`Close(); return;`).
     - Load `_config`, `_configuredTypes`, and `_printerConfig` from `_configService`.
     - Populate printers and `openbisTypeComboBox` immediately from `_configuredTypes`, then refresh openBIS type descriptions asynchronously via `_openBisClient.QueryTypesAsync(...)`:
       - If `ErrorType == "token_expired"`, call `HandleExpiredTokenAsync()`; if renewed, retry `QueryTypesAsync`.
       - If `ErrorType == "connection_error"`, keep cached `DisplayName` values and show a clear non-fatal notice or status warning.
   - Replace `IsSubstance()` with `ResolveTemplateProfile(_currentSelectedTypeCode, _configuredTypes, _currentSampleDetails?.SampleType)` (`public static` for testability) returning `LabelTemplateProfile`, driving both `UpdateFreeTextControlsState()` and `LabelPreviewRenderer.RenderTemplatePreview(...)`.
   - Wire `openbisTypeComboBox_SelectedIndexChanged` and `loadPrintTemplates_Click`: if `result.ErrorType == "token_expired"`, invoke `HandleExpiredTokenAsync()` (opens `SetupSettingsForm` in `ExpiredTokenRecovery` mode) and automatically retry the failed openBIS query once if the user enters a valid new token.
   - Wire `btnSettings_Click`: open `SetupSettingsForm` in `SetupDialogMode.Settings`; on `DialogResult.OK`, reload configs, re-populate printer dropdowns and `openbisTypeComboBox`, and clear stale previews if the active selection changed.
   - Dispose `PictureBox.Image` cleanly and ensure clean resource handling.

- [ ] **Step 4: Update `README.md`**

Document the new `OpenBISLabelStudio/templates/` directory, git-ignored `config/` directory, first-run interactive setup wizard (Connection, Object Types & Template Profiles with live examples, Printer Routing), **Settings** button, and expired-token prompt.

- [ ] **Step 5: Run full verification suite (clean MSBuild Debug + Release, pytest, C# test suite, git status check)**

Run:
```powershell
python -m pytest tests/test_openbis_fetch.py -v
Remove-Item -Recurse -Force OpenBISLabelStudio\bin, OpenBISLabelStudio\obj -ErrorAction SilentlyContinue
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" OpenBISLabelStudio.slnx /t:Build /p:Configuration=Debug /nologo
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" OpenBISLabelStudio.slnx /t:Build /p:Configuration=Release /nologo
& "OpenBISLabelStudio.Tests\bin\Debug\net10.0-windows\OpenBISLabelStudio.Tests.exe"
git status
```
Expected:
- `pytest`: all tests pass (6 passed).
- `MSBuild` Debug & Release: `0 Warning(s)`, `0 Error(s)`.
- `OpenBISLabelStudio.Tests.exe`: all tests pass (`exit code 0`).
- `git status`: no `config/` files or temporary JSON artifacts appear as untracked/modified.

- [ ] **Step 6: Commit**

```powershell
git add OpenBISLabelStudio/Form1.cs OpenBISLabelStudio/Form1.Designer.cs OpenBISLabelStudio.Tests/Program.cs README.md
git commit -m "feat: wire first-run setup, Settings dialog, profile templates, and printer auto-save"
```
