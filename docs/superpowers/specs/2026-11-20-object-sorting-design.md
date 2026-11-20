# Design: Object Sorting in Main Form

**Date:** 2026-11-20
**Branch:** `feat/object-sorting`
**Scope:** Small change (backend sorting service + Form1 row controls + unit tests)
**Status:** Pending User Approval

---

## 1. Intent & Why

Users often work with hundreds of loaded openBIS items inside a single object type (for example `SUBSTANCE` entries such as `0a`, `2a`, `10a`, `100a`, `751a`, or duplicate names with different PermIDs). Allowing instant sorting by **Modification Date**, **Name**, or **PermID** inside `Form1`—backed by pure sorting logic in `OpenBISLabelStudio.Backend`—eliminates manual scrolling without adding openBIS network latency.

## 2. Goals, Non-Goals, and Anti-Goals

- **Goals:**
  - Add a compact **Sort:** selector (`Modification Date`, `Name`, `PermID`) and a **1-click direction toggle (`↑` / `↓`)** inline on row 2 of `Form1`.
  - Keep all sorting rules in `OpenBISLabelStudio.Backend` (`SampleSorter` via `ILabelStudioBackend.SortSamples`), operating immediately in-memory on the loaded `SampleSummaryDto` list.
  - Sort `Name` using natural alphanumeric order (`0a`, `2a`, `10a`, `100a`, `751a`), `PermID` using openBIS timestamp prefix + numeric sequence (`...-8238` before `...-10170`), and `Modification Date` chronologically (UTC/ISO timestamps), breaking ties deterministically by `PermId`.
  - Preserve the currently selected item (by `PermId`) when re-sorting; keep the chosen sort field and direction active across object-type switches in the session.
- **Non-Goals:**
  - Persisting the sort preference across app restarts, re-querying `openbis_fetch.py` on sort change, replacing `openbisObjectsComboBox` with a grid/table, or modifying Python scripts.
- **Anti-Goals:**
  - Never put sorting logic inside `Form1.cs` event handlers, never disrupt async type-load cancellation/control enablement, and never commit `config/` files or credentials.

## 3. Approaches Considered

1. **Option 1 — 3-Choice `Sort:` Dropdown + `↑`/`↓` Direction Button (Selected):**
   Trims `openbisObjectsComboBox` width (`875` → `605`) and places `Sort:` (`Modification Date`, `Name`, `PermID`) + a `↑`/`↓` toggle button aligned to `x = 1040`. Keeps the field list clean and flips ascending/descending in one click.
2. **Option 2 — Single 6-Item `Sort:` Dropdown (Rejected):**
   Combines field and direction into six presets (`Newest Modified`, `Oldest Modified`, `Name (A-Z)`, `Name (Z-A)`, `PermID (Newest)`, `PermID (Oldest)`). Saves one small button (`~20px`), but doubles menu height and requires two clicks to reverse order.

## 4. Architecture & Design Calls

- **Backend Contracts & Service (`OpenBISLabelStudio.Backend`):**
  - Add `SampleSortField` (`ModificationDate`, `Name`, `PermId`) and `SampleSortDirection` (`Ascending`, `Descending`) enums to `BackendContracts.cs`.
  - Add `SampleSorter` (pure static/instance helper in `OpenBISLabelStudio.Backend/Services/`) and expose `IReadOnlyList<SampleSummaryDto> SortSamples(IReadOnlyList<SampleSummaryDto> samples, SampleSortField sortBy, SampleSortDirection direction)` on `ILabelStudioBackend` / `LabelStudioBackend`.
  - Use a deterministic `PermId` tie-breaker (`OrdinalIgnoreCase` fallback if malformed) so duplicate names such as two `0a` entries always appear in predictable order; handle empty/null `ModificationDate` or `Name` values safely.
- **UI Controls & Defaults (`Form1` / `Form1.Designer.cs` — Defaults Proposed for Confirmation):**
  - Row 2 (`y = 62`, right edge aligned at `x = 1040`): `openbisObjectsComboBox` (`Width = 605`), `Sort:` label, `sortByComboBox` (`DropDownStyle = DropDownList`), and `btnSortDirection` (`↑` / `↓` with hover tooltip describing the active direction).
  - **Startup & field-switch defaults:** Startup defaults to `Modification Date` + `↓` (newest first, matching existing behavior). Switching to `Name` defaults to `↑` (`A–Z`); switching to `Modification Date` or `PermID` defaults to `↓` (newest first). Clicking `↑`/`↓` flips direction for the active field.
  - **Selection & async safety:** Changing sort field or direction reorders the cached object list (`_loadedTypeSamples`) through `_backend.SortSamples(...)`, keeps the currently selected `PermId` selected (falling back to index `0` only if missing), and retains the active sort preference when switching OpenBIS object types. `SetSampleQueryControlsEnabled` enables/disables the sort controls alongside `openbisObjectsComboBox` during async loads.

## 5. Verification Plan

- Add unit tests in `OpenBISLabelStudio.Tests` for `SampleSorter` and `LabelStudioBackend.SortSamples` covering: `ModificationDate`, `Name` natural alphanumeric ordering (`0a < 2a < 10a < 100a < 751a`), `PermId` numeric suffix ordering (`...-8238 < ...-10170`), duplicate names, ascending & descending flips, and empty/null metadata.
- Verify MSBuild Debug & Release builds, `OpenBISLabelStudio.Tests.exe` (Debug & Release), and `pytest tests/test_openbis_fetch.py -v`.
