# Hide `managedInternally` System Types Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Exclude openBIS system types marked `managedInternally` from Settings / Setup object type lists cleanly in Python and C# backend layers.

**Architecture:** `openbis_fetch.py`'s `fetch_sample_types` filters out DataFrame rows whose `managedInternally` value is truthy while keeping the existing `{"code", "description"}` dict shape for non-internal types. As defense in depth for direct JSON/C# callers, `OpenBisTypeInfoDto` gains `[JsonProperty("managedInternally")] public bool ManagedInternally { get; set; }`, and `LabelStudioBackend` skips any server type with `ManagedInternally == true` in `FetchAndMergeServerTypesAsync`, `BuildInitialTypeRows`, and `MergeServerTypeDescriptions`.

**Tech Stack:** Python 3.14 + `pytest` (`tests/test_openbis_fetch.py`), .NET 10 C# (`OpenBISLabelStudio.Backend`, `OpenBISLabelStudio.Tests`), MSBuild 18 (`C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe`).

**Spec:** [2026-11-20-hide-system-types-design.md](file:///c:/Users/dafa/Documents/git/openbis-label-studio/docs/superpowers/specs/2026-11-20-hide-system-types-design.md)

## Global Constraints

- Filter `managedInternally` types in backend Python (`OpenBISLabelStudio.Backend/openbis_fetch.py`) and C# backend (`OpenBISLabelStudio.Backend`), never inside `SetupSettingsForm.cs` or `Form1.cs`.
- Preserve the exact existing JSON output keys (`{"code": ..., "description": ...}`) in `fetch_sample_types` for returned non-internal types so existing worker/CLI consumers and `test_openbis_fetch.py:339` stay unchanged.
- Preserve offline/existing saved entries in `BuildInitialTypeRows(serverTypes, existingConfigured)` (`openbis_types.json` fallback).
- Never commit `config/` files or real credentials.

## Review Focus

1. **`NaN` / `NA` values in Python `managedInternally`:** `bool(float('nan'))` evaluates to `True` in Python; `_is_truthy_flag` must treat `None`, `float('nan')`, and objects raising `TypeError` on boolean conversion (like `pandas.NA`) as `False`.
2. **String booleans in `managedInternally`:** `"false"`, `"False"`, `"0"`, `" "` must evaluate to `False` rather than `bool("false") == True`; `"true"`, `"TRUE"`, `"1"`, `"yes"` must evaluate to `True`.
3. **Missing `managedInternally` column/key in `types.df`:** Older mocks or sparse DataFrames omitting `managedInternally` must default cleanly to `False` (included).
4. **C# JSON deserialization and backend sync:** JSON `"managedInternally": true` must bind to `OpenBisTypeInfoDto.ManagedInternally`, and omitted `"managedInternally"` must default to `false`; `FetchAndMergeServerTypesAsync`, `BuildInitialTypeRows`, and `MergeServerTypeDescriptions` must all skip `ManagedInternally == true`.
5. **Existing saved types & Setup/Settings grid preservation:** Saved `ConfiguredOpenBisType` entries in `existingConfigured` must still remain intact in `BuildInitialTypeRows`, while `SetupSettingsForm` syncing against server types never adds `ManagedInternally = true` server types.

---

### Task 1: Filter `managedInternally` Types in `OpenBISLabelStudio.Backend/openbis_fetch.py`

**Files:**
- Modify: `OpenBISLabelStudio.Backend/openbis_fetch.py:196-213`
- Test: `tests/test_openbis_fetch.py`

**Interfaces:**
- Produces (consumed by `OpenBisClient.QueryTypesAsync` / C# `OpenBisTypesResult`):
  - `fetch_sample_types(openbis_url, token, session=None)` returns `{"success": True, "types": [{"code": str, "description": str}, ...]}` excluding any row where `managedInternally` is truthy.

- [ ] **Step 1: Write the failing Python test in `tests/test_openbis_fetch.py`**

Add `test_fetch_sample_types_excludes_managed_internally_types()` covering a mock DataFrame returning rows for:
- `SUBSTANCE` (`managedInternally: False`) -> kept
- `CRYSTAL` (`managedInternally` key omitted) -> kept
- `DEVICE` (`managedInternally: None`) -> kept
- `SUBSTRATE` (`managedInternally: float("nan")`) -> kept
- `WAFER` (`managedInternally: "false"`) -> kept
- `FOIL` (`managedInternally: 0`) -> kept
- `STORAGE` (`managedInternally: True`) -> excluded
- `$STORAGE_POSITION` (`managedInternally: "true"`) -> excluded
- `SYSTEM_ENTRY` (`managedInternally: "TRUE"`) -> excluded
- `PROTOCOL_INTERNAL` (`managedInternally: 1`) -> excluded
Assert `res["success"] is True`, returned `codes == ["SUBSTANCE", "CRYSTAL", "DEVICE", "SUBSTRATE", "WAFER", "FOIL"]`, and every item has exact keys `{"code", "description"}`.

- [ ] **Step 2: Run pytest to verify it fails**

Run: `pytest tests/test_openbis_fetch.py::test_fetch_sample_types_excludes_managed_internally_types -v`
Expected: FAIL because `fetch_sample_types` currently includes `STORAGE`, `$STORAGE_POSITION`, `SYSTEM_ENTRY`, and `PROTOCOL_INTERNAL`.

- [ ] **Step 3: Implement `_is_truthy_flag` and filtering in `OpenBISLabelStudio.Backend/openbis_fetch.py`**

Add helper near `fetch_sample_types`:
```python
def _is_truthy_flag(value):
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
```
In `fetch_sample_types`, right inside `for idx, row in df.iterrows():`:
- Skip row when `_is_truthy_flag(row.get("managedInternally"))` is `True`.

- [ ] **Step 4: Run pytest to verify all Python tests pass**

Run: `pytest tests/test_openbis_fetch.py -v`
Expected: `22 passed`, exit code `0`.

- [ ] **Step 5: Commit Task 1**

```bash
git add OpenBISLabelStudio.Backend/openbis_fetch.py tests/test_openbis_fetch.py
git commit -m "feat: exclude managedInternally sample types in openbis_fetch"
```

---

### Task 2: Add `ManagedInternally` Guard in `OpenBISLabelStudio.Backend` C# Services & Verify Builds

**Files:**
- Modify: `OpenBISLabelStudio.Backend/Models/ConfigModels.cs:132-139`
- Modify: `OpenBISLabelStudio.Backend/Services/LabelStudioBackend.cs:107-115, 547-555, 1427-1435`
- Test: `OpenBISLabelStudio.Tests/Program.cs`

**Interfaces:**
- Modifies: `OpenBISLabelStudio.Backend.Models.OpenBisTypeInfoDto` (`[JsonProperty("managedInternally")] public bool ManagedInternally { get; set; }`)
- Consumes: Task 1 `openbis_fetch.py` filtering + JSON contract compatibility (`"managedInternally"` optional in JSON)

- [ ] **Step 1: Write the failing C# unit tests in `OpenBISLabelStudio.Tests/Program.cs`**

Register `BackendFiltersOutManagedInternallyTypesInBuildAndServerSync` in `Main` and test:
- JSON deserialization of `OpenBisTypesResult`: item with `"managedInternally": true` yields `ManagedInternally == true`; item omitting `"managedInternally"` yields `ManagedInternally == false`.
- `LabelStudioBackend.BuildInitialTypeRows(serverTypes, existingConfigured)` excludes `serverTypes` entries with `ManagedInternally = true` (even on fresh setup) while preserving previously saved `existingConfigured` entries (e.g. `CRYSTAL`).
- `LabelStudioBackend.FetchAndMergeServerTypesAsync` backed by a local `OpenBisClient` stub python returning JSON with `SUBSTANCE` (`managedInternally: false`), `WAFER` (omitted), and `$STORAGE` (`managedInternally: true`) merges only `SUBSTANCE` and `WAFER` (excludes `$STORAGE`, reports `ServerTypeCount == 2`), and preserves saved offline types if passed in `currentRows`.

- [ ] **Step 2: Run Debug build to verify it fails**

Run: `& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" OpenBISLabelStudio.slnx /t:Build /p:Configuration=Debug /v:minimal`
Expected: FAIL with compile error because `OpenBisTypeInfoDto` does not yet define `ManagedInternally`.

- [ ] **Step 3: Implement `ManagedInternally` property and filters in `ConfigModels.cs` and `LabelStudioBackend.cs`**

- In `OpenBISLabelStudio.Backend/Models/ConfigModels.cs` inside `OpenBisTypeInfoDto`:
  add `[JsonProperty("managedInternally")] public bool ManagedInternally { get; set; }`.
- In `OpenBISLabelStudio.Backend/Services/LabelStudioBackend.cs`:
  - `BuildInitialTypeRows`: change null/empty guard to `if (serverType == null || serverType.ManagedInternally || string.IsNullOrWhiteSpace(serverType.Code)) continue;`.
  - `FetchAndMergeServerTypesAsync`: add `&& !t.ManagedInternally` to the `validServerTypes` `.Where(...)` predicate.
  - `MergeServerTypeDescriptions`: add `&& !serverType.ManagedInternally` when updating `descriptionsByCode`.

- [ ] **Step 4: Run Debug & Release builds, C# tests, and Python tests**

Run:
```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" OpenBISLabelStudio.slnx /t:Restore,Build /p:Configuration=Debug /v:minimal
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" OpenBISLabelStudio.slnx /t:Restore,Build /p:Configuration=Release /v:minimal
.\OpenBISLabelStudio.Tests\bin\Debug\net10.0-windows\OpenBISLabelStudio.Tests.exe
.\OpenBISLabelStudio.Tests\bin\Release\net10.0-windows\OpenBISLabelStudio.Tests.exe
pytest tests/test_openbis_fetch.py -v
```
Expected: MSBuild Debug & Release succeed (`exit 0`), all C# tests pass (`0 failed`), `pytest` passes (`22 passed`), and `git status` shows zero tracked `config/` files or credentials.

- [ ] **Step 5: Commit Task 2**

```bash
git add OpenBISLabelStudio.Backend/Models/ConfigModels.cs OpenBISLabelStudio.Backend/Services/LabelStudioBackend.cs OpenBISLabelStudio.Tests/Program.cs
git commit -m "feat: filter ManagedInternally types in LabelStudioBackend"
```
