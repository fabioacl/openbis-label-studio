# Evaporation Temperature Parsing & Adaptive Label Controls Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Support all openBIS evaporation temperature header variants (last non-empty row, assumed `°C` when unitless, non-standard values capped at `10` chars), add synced local-override `Evaporation Temp:` boxes on `Molecule` labels while hiding unused `Free Text` / `Evaporation Temp` controls, and enforce click-outside-only preview commits across both `Evaporation Temp` and `Generic` `Free Text` boxes.

**Architecture:** `openbis_fetch.py` extracts `(formatted_value, raw_value)` from any temperature header column and includes `"evaporationTemperatureRaw"` in sample details. `OpenBISLabelStudio.Backend` surfaces the extracted default/raw values in `SamplePreviewBundleDto`, adds `RenderMoleculeEvaporationTemperatureAsync` to re-render `SmallBox12mm` + `Magnet12mm` from the active sample snapshot without mutating it or openBIS, and applies any overridden temperature in `PrintSelectedLabelsAsync`. `Form1` places synced `Evaporation Temp:` boxes inside Small Box and Magnet (hiding all unused boxes per profile) and commits edits on both `.Leave` (triggered reliably by clicking non-selectable surfaces via recursive `MouseDown` focus clearing) and `Enter`.

**Tech Stack:** Python 3 (`openbis_fetch.py`, `pytest`), .NET 10 Windows Forms (`net10.0-windows`, C#), MSBuild 18 (`C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe`), standalone WinForms/unit test runner (`OpenBISLabelStudio.Tests`).

**Spec:** [2026-11-23-evaporation-temperature-design.md](file:///c:/Users/dafa/Documents/git/openbis-label-studio/docs/superpowers/specs/2026-11-23-evaporation-temperature-design.md)

## Global Constraints

- Work exclusively on branch `feat/evaporation-temperature`; never commit `config/` files, real URLs, or PAT tokens.
- Keep openBIS strictly read-only: UI edits override preview and print output locally only.
- `Form1` must only reference `OpenBISLabelStudio.Backend.Contracts` types (never `Backend.Services`, Python scripts, or secrets).
- Clicking outside any editable textbox—including on `PictureBox` previews, `GroupBox`es, `Label`s, or empty form background—must commit without rendering on keystrokes; keep window `ClientSize` at `1080 × 530`.

## Review Focus

1. **`None` / whitespace / `NaN` padding rows in `Spreadsheet.data`:** Empty bottom rows in pyBIS spreadsheets contain `None` or `""`; reversing over `data` must never stringify `None` into `"None"` and must select the last truly non-empty cell in the temperature column.
2. **Non-temperature headers containing `"t"` or `"temp"`:** Header matching must match exact `"t"` (optional unit) or word `"temperature"` (optional unit) case-insensitively without matching unrelated headers such as `"Datetime"`, `"Template"`, or `"Slot"`.
3. **Cross-sample & cross-type snapshot isolation:** Editing `Evaporation Temp` for sample A, switching object types or calling `ClearActiveSample`, or sending Molecule temperature selections against a stale or `Generic` sample must never leak previous sample overrides or mutate the stored baseline `ActiveSampleState.Details`.
4. **Synced input recursion & unchanged-text renders:** Programmatically mirroring text between `smallBoxEvapTempBox` and `magnetEvapTempBox` or clicking in/out of any textbox without changing text must never cause recursive events or duplicate b-PAC preview renders.
5. **WinForms non-selectable click targets (`PictureBox`, `GroupBox`, `Label`, `Form`):** Clicking non-selectable controls while a textbox is focused must clear focus to trigger `.Leave` for **both** Molecule `Evaporation Temp:` and Generic `Free Text:` inputs, and `Form1` layout bounds checks must stay overlap-free across all profile states.

---

### Task 1: Python Header/Value Normalization & Raw Temperature Field

**Files:**
- Modify: `OpenBISLabelStudio.Backend/openbis_fetch.py:153-195,343-413`
- Test: `tests/test_openbis_fetch.py`

**Interfaces:**
- Produces (consumed by Task 2):
  - `extract_evaporation_temperature_info(spreadsheet) -> tuple[str, str]` returning `(formatted_temperature, raw_cell_text)`
  - `extract_evaporation_temperature(spreadsheet) -> str` returning `formatted_temperature`
  - `fetch_sample_details(...)` JSON `details` keys `"evaporationTemperature"` and `"evaporationTemperatureRaw"`

- [ ] **Step 1: Write the failing pytest tests in `tests/test_openbis_fetch.py`**

Add tests exercising:
  - Header variations: `["instrument", "T"]`, `["Instrument", "Temperature"]`, `["Device", "Temperature (°C)"]`, `["Instrument", "Temperature [K]"]`, plus unrelated leading column `["Datetime", "Template", "Temperature"]` verifying `"Datetime"`/`"Template"` are skipped.
  - Multiple rows & bottom `None` / `""` / `"  "` padding rows: selects the last non-empty row in the temperature column (`[["SRD", "140"], ["VT", "118"], [None, None]]` -> `"118°C"`).
  - Unit assumption & normalization: unitless `"450"` -> `"450°C"`; negative/decimal `"-12.5"` -> `"-12.5°C"`; header unit `Temperature (°C)` with `"250"` -> `"250°C"`; header unit `Temperature [K]` with `"520"` -> `"520 K"`; cell Celsius variants `"200C"`, `"407℃"`, `"60°c"`, `"140 °C"` -> `"200°C"`, `"407°C"`, `"60°C"`, `"140°C"`; Kelvin `"500 K"` / `"500k"` -> `"500 K"`; Fahrenheit `"300°F"` -> `"300°F"`.
  - Non-standard text & intervals capped at 10 chars (with `evaporationTemperatureRaw` intact): `"200 - 210"` -> `"200 - 210"`, `"400-500°C"` -> `"400-500°C"`, `">400°C"` -> `">400°C"`, `"RT"` -> `"RT"`, `"250°C (1 A/min)"` -> `("250°C (1 A", "250°C (1 A/min)")`, `"stable up to 240°C"` -> `("stable up", "stable up to 240°C")`, `"no subli"` -> `"no subli"`, `"ST not reached"` -> `("ST not rea", "ST not reached")`, `"??"` -> `"??"`, `"some subblimation rate above 450C"` -> `("some subbl", "some subblimation rate above 450C")`.
  - Missing/empty spreadsheet returns `("", "")` and `fetch_sample_details` populates both `"evaporationTemperature"` and `"evaporationTemperatureRaw"`.

- [ ] **Step 2: Run pytest to verify the new tests fail**

Run: `C:\Users\dafa\AppData\Local\anaconda3\envs\openbis_env\python.exe -m pytest tests/test_openbis_fetch.py -v`
Expected: FAIL on new tests (`extract_evaporation_temperature_info` missing / strict `Temperature [Unit]` returning `""`).

- [ ] **Step 3: Implement `extract_evaporation_temperature_info` and wire `fetch_sample_details` in `OpenBISLabelStudio.Backend/openbis_fetch.py`**

Implement header lookup (`^t(?:\s*[\[(]\s*([^\])]+?)\s*[\])])?$` or `\btemperature\b(?:\s*[\[(]\s*([^\])]+?)\s*[\])])?$` case-insensitive), safe cell extraction skipping `None`/`""`/NaN from bottom up, unit normalization helper (assuming `°C` on unitless numbers), `[:10].rstrip()` cap for non-standard values, keep `extract_evaporation_temperature(spreadsheet)` delegating to `extract_evaporation_temperature_info(spreadsheet)[0]`, and emit `"evaporationTemperatureRaw"` in `fetch_sample_details`.

- [ ] **Step 4: Run pytest to verify all Python tests pass**

Run: `C:\Users\dafa\AppData\Local\anaconda3\envs\openbis_env\python.exe -m pytest tests/test_openbis_fetch.py -v`
Expected: PASS (all existing 22 + all new tests pass).

- [ ] **Step 5: Commit Task 1**

```bash
git add OpenBISLabelStudio.Backend/openbis_fetch.py tests/test_openbis_fetch.py
git commit -m "feat: normalize all openBIS temperature headers and values in Python fetcher"
```

---

### Task 2: Backend Contracts, Snapshot Cloning, Preview Re-Render & Print Override

**Files:**
- Modify: `OpenBISLabelStudio.Backend/Models/ConfigModels.cs:220-230` (`SampleDetailsDto.EvaporationTemperatureRaw`)
- Modify: `OpenBISLabelStudio.Backend/Contracts/BackendContracts.cs:74-96,302-326` (`RenderMoleculeEvaporationTemperatureAsync`, `SamplePreviewBundleDto`, `PrintSlotSelection.EvaporationTemperature`)
- Modify: `OpenBISLabelStudio.Backend/Services/LabelStudioBackend.cs:1009-1174`
- Test: `OpenBISLabelStudio.Tests/Program.cs` (update `StubLabelStudioBackend` + extend `BackendLoadSamplePreviewsAndFreeTextIsolationPreventsStaleState` / add `BackendMoleculeTemperatureOverrideIsolationAndPrintFlow`)

**Interfaces:**
- Consumes from Task 1: `"evaporationTemperature"` and `"evaporationTemperatureRaw"` JSON fields parsed into `SampleDetailsDto`.
- Produces (consumed by Task 3):
  - `SamplePreviewBundleDto.EvaporationTemperature` (`string`, default `""`)
  - `SamplePreviewBundleDto.EvaporationTemperatureRaw` (`string`, default `""`)
  - `PrintSlotSelection.EvaporationTemperature` (`string?`, default `null`)
  - `Task<IReadOnlyDictionary<LabelTemplateType, byte[]>> ILabelStudioBackend.RenderMoleculeEvaporationTemperatureAsync(string evaporationTemperature, CancellationToken ct = default)`

- [ ] **Step 1: Write the failing backend tests and update `StubLabelStudioBackend` in `OpenBISLabelStudio.Tests/Program.cs`**

Register `BackendMoleculeTemperatureOverrideIsolationAndPrintFlow` in `Main`, extend `StubLabelStudioBackend` cleanly with `RenderMoleculeEvaporationTemperatureAsync` and `PrintSelectedLabelsCalls`, and assert:
  - Loading a `Molecule` sample returns `bundle.EvaporationTemperature` (`"250°C (1 A"`) and `bundle.EvaporationTemperatureRaw` (`"250°C (1 A/min)"`), and returns empty dictionary from `RenderMoleculeEvaporationTemperatureAsync` before sample load or after `ClearActiveSample` / `LoadObjectsForTypeAsync`.
  - Calling `RenderMoleculeEvaporationTemperatureAsync("250°C")` on an active `Molecule` sample renders valid PNGs for **only** `SmallBox12mm` and `Magnet12mm` (`Count == 2`), does not mutate the stored baseline `Details.EvaporationTemperature` (a later `PrintSelectedLabelsAsync` without override prints `"250°C (1 A"`, whereas passing `EvaporationTemperature = "310°C"` in `PrintSlotSelection` passes `"310°C"` to `CapturingLabelPreviewRenderer.PrintLabelsAsync`).
  - Calling `RenderMoleculeEvaporationTemperatureAsync` on an active `Generic` sample returns an empty dictionary and triggers 0 renders; passing `EvaporationTemperature` in `PrintSlotSelection` for a `Generic` sample leaves `details.EvaporationTemperature` untouched.

- [ ] **Step 2: Build Debug to verify RED fail**

Run: `& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" OpenBISLabelStudio.slnx /t:Build /p:Configuration=Debug /v:minimal`
Expected: FAIL with missing `RenderMoleculeEvaporationTemperatureAsync`, `EvaporationTemperatureRaw`, and `PrintSlotSelection.EvaporationTemperature`.

- [ ] **Step 3: Implement contract additions & `LabelStudioBackend` support**

- Add `EvaporationTemperatureRaw` (`[JsonProperty("evaporationTemperatureRaw")]`) to `SampleDetailsDto` in `ConfigModels.cs`.
- Add `EvaporationTemperature` & `EvaporationTemperatureRaw` to `SamplePreviewBundleDto`, `string? EvaporationTemperature { get; set; }` to `PrintSlotSelection`, and `RenderMoleculeEvaporationTemperatureAsync` to `ILabelStudioBackend` in `BackendContracts.cs`.
- In `LabelStudioBackend.cs`:
  - Populate `EvaporationTemperature` and `EvaporationTemperatureRaw` on returned `SamplePreviewBundleDto` in `LoadSamplePreviewsAsync`.
  - Add a private `CloneDetailsWithEvaporationTemperature(SampleDetailsDto source, string evaporationTemperature)` helper (copying all DTO fields cleanly so active state stays immutable).
  - Implement `RenderMoleculeEvaporationTemperatureAsync(string evaporationTemperature, CancellationToken ct)`: return empty dictionary when `_activeSample == null` or `_activeSample.Profile != LabelTemplateProfile.Molecule`; otherwise render `SmallBox12mm` & `Magnet12mm` concurrently via `_previewRenderer.RenderTemplatePreviewPngAsync` with fallback handling matching `LoadSamplePreviewsAsync`.
  - In `PrintSelectedLabelsAsync`: when `snapshot.Profile == LabelTemplateProfile.Molecule` and any non-null `selection.EvaporationTemperature` override is provided, clone `snapshot.Details` with that value (`FirstOrDefault(s => s?.EvaporationTemperature != null)?.EvaporationTemperature`) before calling `_previewRenderer.PrintLabelsAsync`.

- [ ] **Step 4: Build Debug & run tests to verify PASS**

Run:
```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" OpenBISLabelStudio.slnx /t:Restore,Build /p:Configuration=Debug /v:minimal
.\OpenBISLabelStudio.Tests\bin\Debug\net10.0-windows\OpenBISLabelStudio.Tests.exe
```
Expected: All C# tests PASS with `0 failed`.

- [ ] **Step 5: Commit Task 2**

```bash
git add OpenBISLabelStudio.Backend/Models/ConfigModels.cs OpenBISLabelStudio.Backend/Contracts/BackendContracts.cs OpenBISLabelStudio.Backend/Services/LabelStudioBackend.cs OpenBISLabelStudio.Tests/Program.cs
git commit -m "feat: add backend evaporation temperature override render and print support"
```

---

### Task 3: `Form1` Adaptive Controls, Synced Boxes & Click-Outside Commit

**Files:**
- Modify: `OpenBISLabelStudio/Form1.Designer.cs` (add `smallBoxEvapTempLabel`, `smallBoxEvapTempBox`, `magnetEvapTempLabel`, `magnetEvapTempBox` inside existing `1080 × 530` cards)
- Modify: `OpenBISLabelStudio/Form1.cs` (adaptive visibility, synced Evap Temp inputs + raw tooltip, click-anywhere-outside commit for both Molecule Evap Temp and Generic Free Text, unchanged-text guard, print integration)
- Test: `OpenBISLabelStudio.Tests/Program.cs` (add `Form1AdaptiveTemperatureAndFreeTextClickOutsideCommitBehavior` and extend layout/overlap checks cleanly)

**Interfaces:**
- Consumes from Task 2:
  - `SamplePreviewBundleDto.EvaporationTemperature` / `EvaporationTemperatureRaw`
  - `ILabelStudioBackend.RenderMoleculeEvaporationTemperatureAsync(string evaporationTemperature, CancellationToken ct)`
  - `PrintSlotSelection.EvaporationTemperature`

- [ ] **Step 1: Write the failing WinForms UI tests in `OpenBISLabelStudio.Tests/Program.cs`**

Register `Form1AdaptiveTemperatureAndFreeTextClickOutsideCommitBehavior` in `Main`, extend `Form1HasSettingsButtonWithoutOverlaps` (verifying `smallBoxEvapTempBox` / `magnetEvapTempBox` stay inside their group boxes and don't intersect previews or printer dropdowns, and start hidden before a type is selected), and assert in the new UI test:
  - Before type selection (`PLACEHOLDER_TYPE`): all 4 boxes (`smallBoxEvapTempBox`, `magnetEvapTempBox`, `magnetFreeTextBox`, `flaskFreeTextBox`) and their labels are hidden (`Visible == false`) and disabled (`Enabled == false`).
  - Selecting `SUBSTANCE` (`Molecule`): `smallBoxEvapTempBox` & `magnetEvapTempBox` are visible & enabled; `magnetFreeTextBox` & `flaskFreeTextBox` (plus labels) are hidden (`Visible == false`) and disabled. Loading sample initializes both Evap Temp boxes to `bundle.EvaporationTemperature` (`"250°C (1 A"`) and sets tooltip showing `"250°C (1 A/min)"` when raw differs.
  - Typing `"250°C"` into `smallBoxEvapTempBox` immediately syncs `magnetEvapTempBox.Text == "250°C"` with **0** backend render calls during keystrokes; clicking a non-selectable surface (e.g. firing `MouseDown` on `smallBoxLabelPreview` / `Form1`) triggers `.Leave` and invokes `RenderMoleculeEvaporationTemperatureAsync("250°C")` **once**; clicking outside again without editing performs **0** additional renders.
  - Clicking `btnPrintLabel` sends `EvaporationTemperature = "250°C"` in Molecule print selections.
  - Switching to `SUBSTRATE` (`Generic`): hides `smallBoxEvapTemp*` & `magnetEvapTemp*`, shows `magnetFreeText*` & `flaskFreeText*`; loading sample and typing `"New Note"` in `flaskFreeTextBox` performs **0** renders until clicking `flaskLabelPreview` (`MouseDown` -> focus leave -> `RenderSlotWithFreeTextAsync(Flask24mm, "New Note")` invoked once), and clicking again unchanged triggers **0** extra renders.

- [ ] **Step 2: Build Debug to verify RED fail**

Run: `& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" OpenBISLabelStudio.slnx /t:Build /p:Configuration=Debug /v:minimal`
Expected: FAIL because `smallBoxEvapTempBox`, `magnetEvapTempBox`, etc. do not exist on `Form1`.

- [ ] **Step 3: Update `OpenBISLabelStudio/Form1.Designer.cs` and `OpenBISLabelStudio/Form1.cs`**

In `Form1.Designer.cs`:
- Add `smallBoxEvapTempLabel` (`Location(15, 170)`, `"Evaporation Temp:"`) + `smallBoxEvapTempBox` (`Location(15, 195)`, `Size(270, 27)`) inside `smallBoxLabelGroupBox` (adjust `smallBoxPrinterLabel` / `smallBoxPrinterComboBox` TabIndex cleanly).
- Add `magnetEvapTempLabel` (`Location(15, 170)`, `"Evaporation Temp:"`) + `magnetEvapTempBox` (`Location(15, 195)`, `Size(300, 27)`) inside `magnetLabelGroupBox` (matching `magnetFreeText*` bounds cleanly).
- Hook `TextChanged`, `Leave`, and `KeyDown` for both `smallBoxEvapTempBox` and `magnetEvapTempBox`.

In `Form1.cs`:
- Replace `UpdateFreeTextControlsState()` cleanly with `UpdateTemplateInputControlsState()` implementing the exact profile visibility & enabled state rules (`None` vs `Molecule` vs `Generic`), keeping `_isSyncingEvapTemp`, `_lastCommittedEvapTemp`, `_lastCommittedMagnetFreeText`, and `_lastCommittedFlaskFreeText`.
- Populate Molecule temp boxes & raw tooltip from `bundle` in `LoadAndStreamSamplePreviewsAsync` and clear them in `ClearAllPreviews()`.
- Sync `smallBoxEvapTempBox` <-> `magnetEvapTempBox` cleanly on `TextChanged` without triggering b-PAC preview renders.
- Wire recursive `MouseDown` click-outside commit (`RegisterClickOutsideFocusClear(Control root)`) in constructor so clicking outside any active `TextBox` (including `PictureBox`, `GroupBox`, `Label`, `Form1` empty space) clears `ActiveControl = null` to trigger `.Leave`, committing `Evap Temp` (updating both `SmallBox12mm` and `Magnet12mm`) or `Free Text` (`Magnet12mm` / `Flask24mm`) only when the value actually changed since the last render.
- Pass `EvaporationTemperature = smallBoxEvapTempBox.Text` on `Molecule` selections in `btnPrintLabel_Click_1`.

- [ ] **Step 4: Run Debug & Release MSBuild, all C# unit/UI tests, and `pytest`**

Run:
```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" OpenBISLabelStudio.slnx /t:Restore,Build /p:Configuration=Debug /v:minimal
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" OpenBISLabelStudio.slnx /t:Restore,Build /p:Configuration=Release /v:minimal
.\OpenBISLabelStudio.Tests\bin\Debug\net10.0-windows\OpenBISLabelStudio.Tests.exe
.\OpenBISLabelStudio.Tests\bin\Release\net10.0-windows\OpenBISLabelStudio.Tests.exe
C:\Users\dafa\AppData\Local\anaconda3\envs\openbis_env\python.exe -m pytest tests/test_openbis_fetch.py -v
```
Expected: Both Debug & Release MSBuild succeed (`exit 0`), all C# tests PASS in Debug & Release (`0 failed`), `pytest` passes cleanly.

- [ ] **Step 5: Commit Task 3**

```bash
git add OpenBISLabelStudio/Form1.Designer.cs OpenBISLabelStudio/Form1.cs OpenBISLabelStudio.Tests/Program.cs
git commit -m "feat: add synced evaporation temp UI controls and click-outside commit"
```
