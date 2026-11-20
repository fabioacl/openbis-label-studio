# Design: Hide `managedInternally` System Types in Settings

**Date:** 2026-11-20
**Branch:** `feat/hide-system-types` (off `main`)
**Scope:** Small change (Python `fetch_sample_types` + C# backend type-merge guard + unit tests)
**Status:** Pending User Approval

---

## 1. Intent & Why

openBIS returns core/plugin system-managed sample types (`managedInternally == True`) alongside laboratory object types. These internal types are never used for printing labels and clutter the **Object Types & Templates** table in `SetupSettingsForm`. Excluding them in the backend keeps Settings clean for laboratory types only.

## 2. Goals, Non-Goals, and Anti-Goals

- **Goals:**
  - Exclude any openBIS sample type with `managedInternally == true` from `fetch_sample_types` in `OpenBISLabelStudio.Backend/openbis_fetch.py` while keeping the `{"code", "description"}` output shape unchanged for user types.
  - Also honor `[JsonProperty("managedInternally")]` on `OpenBisTypeInfoDto` in `LabelStudioBackend` (`FetchAndMergeServerTypesAsync` and `BuildInitialTypeRows`) so custom/C# backend payloads flagged `ManagedInternally = true` are filtered consistently.
  - Preserve backward compatibility when `managedInternally` is absent, `None`, or `NaN` in DataFrames/mocks, and keep existing locally saved `openbis_types.json` fallback behavior intact.
- **Non-Goals:**
  - Adding a "Show system types" UI toggle (user confirmed `managedInternally` types are never used).
  - Filtering non-internal openBIS types by `code` heuristics or `listable`, or filtering in `SetupSettingsForm.cs`.
- **Anti-Goals:**
  - Never treat `float('nan')` or string `"false"` as `True` in Python boolean checks, never break existing `openbis_types.json` offline loading, and never commit `config/` files or credentials.

## 3. Approaches Considered

1. **Filter at Backend Data Boundary (`openbis_fetch.py` + `LabelStudioBackend`) (Proposed):**
   Skips `managedInternally == True` rows inside `fetch_sample_types` (both CLI `--types` and persistent `--worker` modes) and skips `ManagedInternally == true` items in `LabelStudioBackend` (`FetchAndMergeServerTypesAsync`, `BuildInitialTypeRows`, and `MergeServerTypeDescriptions`). Keeps `SetupSettingsForm` strictly presentation-only and preserves existing Python JSON output fields.
2. **Filter Only in `SetupSettingsForm.cs` UI (Rejected):**
   Would require piping extra flags into UI DTOs and violate the frontend/backend split (`Form1`/`SetupSettingsForm` never hold openBIS business filtering logic).

## 4. Design Calls & Verification

- **`openbis_fetch.py` (`fetch_sample_types`):** Skip rows where helper `_is_truthy_flag(row.get("managedInternally"))` returns `True` (`True`, non-NaN truthy numbers, or `"true"`/`"1"`/`"yes"` case-insensitively; `None`, `NaN`, `False`, `"false"`, or missing column -> `False`).
- **`OpenBISLabelStudio.Backend`:** Add `ManagedInternally` (`bool`, default `false`) to `OpenBisTypeInfoDto` and ignore `serverType.ManagedInternally` entries across `FetchAndMergeServerTypesAsync`, `BuildInitialTypeRows`, and `MergeServerTypeDescriptions`.
- **Verification:** Add Python unit tests in `tests/test_openbis_fetch.py` (filtering `managedInternally=True`/`"true"` while keeping `False`/`None`/`NaN`/`"false"` and missing columns) and C# unit tests in `OpenBISLabelStudio.Tests/Program.cs` (filtering `ManagedInternally = true` in `BuildInitialTypeRows` and `FetchAndMergeServerTypesAsync`); run Debug/Release MSBuild, `OpenBISLabelStudio.Tests.exe` (Debug & Release), and `pytest tests/test_openbis_fetch.py -v`.
