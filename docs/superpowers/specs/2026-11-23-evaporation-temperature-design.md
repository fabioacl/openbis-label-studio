# Design: Evaporation Temperature Parsing & Adaptive Label Controls

**Date:** 2026-11-23
**Branch:** `feat/evaporation-temperature`
**Scope:** Small change (Python openBIS spreadsheet parser + C# backend override/re-render support + adaptive synced WinForms controls & click-outside commit + unit/UI tests)
**Status:** Pending User Approval

---

## 1. Intent & Why

In the live openBIS `SUBSTANCE` database (`239` populated `evaporation_temperatures` tables), spreadsheet headers no longer use bracketed `Temperature [Unit]`; they appear across variations such as `T`, `temperature`, `Temperature`, `Temperature (°C)`, and `Temperature [Unit]`. Additionally, many entries omit units, have multiple instrument rows, or contain non-standard values (intervals like `200 - 210`, inequalities like `>400°C`, room temperature `RT`, rate notes like `250°C (1 A/min)`, or text such as `no subli` / `ST not reached` / `??`).

Updating the extraction logic to handle all header variants, assume `°C` for unitless numeric entries, default to the last non-empty row, cap non-standard text to `10` characters, and provide synced local-override **Evaporation Temp** textboxes (while hiding unused `Free Text` / `Evaporation Temp` controls per label profile) lets users print accurate labels without mutating openBIS records.

## 2. Goals, Non-Goals, and Anti-Goals

- **Goals:**
  - Parse any temperature column header (`T`, `temperature`, `Temperature`, `Temperature (°C)`, `Temperature [Unit]`, case-insensitive) and read the **last non-empty cell** in that column.
  - Assume **Celsius (`°C`)** when a numeric cell has no unit in either the header or the cell (e.g. `140` → `140°C`); normalize Celsius unit variants (`C`, `°c`, `℃`, `°C`) to `°C` and preserve explicit non-Celsius units such as `500 K`.
  - Keep non-standard text/intervals/notes (anything other than a single numeric value with optional unit) without inventing a unit, **trimmed and truncated to at most `10` characters** (while passing the full raw cell text for tooltip context).
  - Show synced **Evaporation Temp** textboxes inside **Small Box 12mm Label** and **Magnet 12mm Label** when a `Molecule` profile (`SUBSTANCE`) is active, hiding all unused **Free Text** controls (`Magnet` + `Flask`).
  - Show **Free Text** textboxes inside **Magnet 12mm Label** and **Flask 24mm Label** only when a `Generic` profile is active, hiding **Evaporation Temp** controls (`Small Box` stays clean); hide all optional boxes when no type is selected.
  - Only re-render label previews when the user **clicks outside** an editable textbox (including non-selectable surfaces such as `PictureBox`, `GroupBox`, `Label`, or the form background) or presses `Enter`, never on every keystroke — applied consistently to **both** Molecule `Evaporation Temp` boxes and Generic `Free Text` boxes.
  - Allow users to override the printed temperature locally for preview & print without writing back to openBIS.
- **Non-Goals:**
  - Writing edited temperatures back to openBIS spreadsheets.
  - Adding an evaporation temperature field to `Flask 24mm Label` (`template_molecule_flask_24mm.lbx` does not include `evaporationTemperature`).
  - Resizing `Form1` (`1080 × 530` layout remains unchanged).
- **Anti-Goals:**
  - Never mutate openBIS records, never fire b-PAC COM preview renders on individual keystrokes (`TextChanged`), and never allow empty/stale snapshot overrides across sample switches.

## 3. Approaches Considered

1. **Adaptive In-Card Slot Visibility with Synced `Evaporation Temp` Boxes (Selected):**
   Keeps `Form1` at `1080 × 530`, uses the open `y = 170..222` slot inside `Small Box 12mm` + swaps the `Magnet 12mm` `y = 170..222` slot cleanly between `Evaporation Temp:` (`Molecule`) and `Free Text:` (`Generic`), and hides unused controls (`Visible = false`) such as `Flask` `Free Text` on Molecule types and `Small Box` `Evaporation Temp` on Generic types. Typing in either Molecule temperature box stays synced across both labels.
2. **Shared `Evaporation Temp` Row Above the Cards (Rejected):**
   Would require increasing `Form1` height (`530 → ~565`) and keeping disabled `(N/A for Molecule templates)` `Free Text` boxes visible on `Magnet` and `Flask`.
3. **Temperature Box in Small Box Card Only or Independent Per-Label Boxes (Rejected):**
   A single Small Box textbox looks scoped only to Small Box even though Magnet prints `evaporationTemperature` too; unsynced independent boxes force double entry for the same substance temperature.

## 4. Architecture & Design Calls

- **Python Parser (`OpenBISLabelStudio.Backend/openbis_fetch.py`):**
  - Replace the strict `Temperature [Unit]` check in `extract_evaporation_temperature(spreadsheet)` with a helper returning `(formatted_value, raw_value)` (keeping `extract_evaporation_temperature(spreadsheet) -> str` returning `formatted_value` for backwards compatibility):
    - Find the first header column whose trimmed lowercase name equals `"t"` or starts with `"temperature"` (capturing any bracketed/parenthesized header unit such as `[K]`, `(°C)`).
    - Scan `data` in reverse to select the last non-empty trimmed cell in that column (`raw_value`).
    - **Single numeric value** (`^[+-]?\d+(?:[.,]\d+)?$`): append normalized header unit if present, else `°C` (*My call:* format Celsius values compactly without space as e.g. `140°C` / `450°C` matching the majority of existing openBIS records, and preserve spaced Kelvin like `500 K`).
    - **Number + simple unit** (`°C`, `C`, `°c`, `℃`, `K`, `°F`, `F`): normalize Celsius symbols to `°C` (e.g., `200C` → `200°C`, `407℃` → `407°C`, `60°c` → `60°C`); normalize Kelvin/Fahrenheit cleanly (`500 K`).
    - **Non-standard text/intervals/notes** (e.g. `200 - 210`, `400-500°C`, `>400°C`, `RT`, `250°C (1 A/min)`, `no subli`, `ST not reached`, `??`, `some subblimation rate above 450C`): keep as-is trimmed and truncated to **`10` characters max** (`raw_value[:10]`).
  - Add `"evaporationTemperatureRaw": raw_value` to `fetch_sample_details` payload.
- **Backend Contracts & Services (`OpenBISLabelStudio.Backend`):**
  - `SampleDetailsDto` (`Models/ConfigModels.cs`): add optional `EvaporationTemperatureRaw`.
  - `SamplePreviewBundleDto` (`Contracts/BackendContracts.cs`): add `EvaporationTemperature` and `EvaporationTemperatureRaw` populated from fetched details so `Form1` initializes Molecule boxes cleanly.
  - `PrintSlotSelection` (`Contracts/BackendContracts.cs`): add optional `string? EvaporationTemperature { get; set; }` (`null` keeps fetched sample default; non-null overrides printed temperature for `Molecule` jobs without touching openBIS).
  - `ILabelStudioBackend` / `LabelStudioBackend`:
    - Add `Task<IReadOnlyDictionary<LabelTemplateType, byte[]>> RenderMoleculeEvaporationTemperatureAsync(string evaporationTemperature, CancellationToken ct = default)` which clones the active `SampleDetailsDto` snapshot with the overridden `EvaporationTemperature` (without mutating the baseline snapshot or openBIS) and renders `SmallBox12mm` + `Magnet12mm` in parallel when `snapshot.Profile == LabelTemplateProfile.Molecule`.
    - In `PrintSelectedLabelsAsync`, when `snapshot.Profile == LabelTemplateProfile.Molecule`, clone/apply the overridden `EvaporationTemperature` (if supplied on any selection) per job before invoking `_previewRenderer.PrintLabelsAsync`.
  - *My call on manual typing length:* cap extracted non-standard defaults from openBIS at `10` characters in Python, while leaving manual user edits in the UI textboxes uncapped (`MaxLength = 0`) so users can enter exact overrides freely if needed.
- **Frontend UI & Focus Commit (`OpenBISLabelStudio/Form1.Designer.cs` & `Form1.cs`):**
  - Add `smallBoxEvapTempLabel` + `smallBoxEvapTempBox` inside `smallBoxLabelGroupBox` (`y = 170..222`) and `magnetEvapTempLabel` + `magnetEvapTempBox` inside `magnetLabelGroupBox` (`y = 170..222`, sharing coordinates cleanly with `magnetFreeText*`).
  - Adaptive visibility (`UpdateTemplateInputControlsState`):
    - No type selected → hide & disable all 4 optional fields (`smallBoxEvapTemp*`, `magnetEvapTemp*`, `magnetFreeText*`, `flaskFreeText*`).
    - `Molecule` profile → show & enable `smallBoxEvapTemp*` + `magnetEvapTemp*`; hide & disable `magnetFreeText*` + `flaskFreeText*` (`Visible = false`, `Enabled = false`).
    - `Generic` profile → show & enable `magnetFreeText*` + `flaskFreeText*`; hide & disable `smallBoxEvapTemp*` + `magnetEvapTemp*` (`Visible = false`, `Enabled = false`).
  - **Synced Temperature Boxes:** typing mirrors between `smallBoxEvapTempBox` and `magnetEvapTempBox` via `_isSyncingEvapTemp`, setting hover tooltip when `EvaporationTemperatureRaw` differs from the pre-filled value.
  - **Commit on Click-Outside (No Keystroke Renders):** Attach a recursive `MouseDown` handler across `Form1` and non-`TextBox` child controls (including `PictureBox`, `GroupBox`, `Label`, and blank form space) that clears focus (`ActiveControl = null`) whenever the user clicks outside the currently focused `TextBox`, ensuring `.Leave` fires reliably even when clicking non-selectable WinForms controls (plus `Enter` commits cleanly). Guard commit handlers for both `Evaporation Temp` and `Free Text` so unchanged text does not trigger duplicate renders.

## 5. Verification Plan

- Extend `tests/test_openbis_fetch.py` to test all header variants (`T`, `temperature`, `Temperature`, `Temperature (°C)`, `Temperature [K]`), last-row selection with multiple rows, bare numeric Celsius assumption, `C`/`℃`/`°c`/`K` normalization, empty rows, and non-standard text/intervals truncated to `10` characters + raw field preservation.
- Extend `OpenBISLabelStudio.Tests/Program.cs` for:
  - `LabelStudioBackend.RenderMoleculeEvaporationTemperatureAsync` (requires active `Molecule` sample, returns empty when unselected or `Generic`, isolates `Flask`, resets on `ClearActiveSample`) and `PrintSelectedLabelsAsync` overriding Molecule `EvaporationTemperature`.
  - `Form1` UI tests verifying adaptive visibility (`Molecule` shows synced Evap Temp boxes & hides Free Text boxes including Flask; `Generic` shows Free Text boxes & hides Evap Temp boxes), synced typing without premature renders, and click-outside (`PictureBox`/background `MouseDown` → `.Leave`) triggering render once for both Evap Temp and Generic Free Text.
- Run Debug & Release MSBuild + `OpenBISLabelStudio.Tests.exe` and `pytest tests/test_openbis_fetch.py -v` after final implementation.
