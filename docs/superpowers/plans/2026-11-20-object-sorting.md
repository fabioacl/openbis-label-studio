# Object Sorting Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add in-memory sorting by Modification Date, Name, or PermID (with an `↑`/`↓` direction toggle) for loaded openBIS objects in `OpenBISLabelStudio`, backed by pure backend logic and unit/UI tests.

**Architecture:** Sorting rules live in a pure `SampleSorter` service in `OpenBISLabelStudio.Backend` exposed through `ILabelStudioBackend.SortSamples`, using `SampleSortField` and `SampleSortDirection` enums in `BackendContracts.cs`. `Form1` narrows `openbisObjectsComboBox`, adds a 3-choice `Sort:` dropdown and `↑`/`↓` button on row 2 aligned to `x = 1040`, preserves the active `PermId` selection on re-sort, and retains the chosen sort preference across object-type switches without triggering openBIS network calls.

**Tech Stack:** .NET 10 Windows Forms (`net10.0-windows`, C#), MSBuild 18 (`C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe`), standalone WinForms/unit test runner (`OpenBISLabelStudio.Tests`), Python `pytest` (`tests/test_openbis_fetch.py`).

**Spec:** [2026-11-20-object-sorting-design.md](file:///c:/Users/dafa/Documents/git/openbis-label-studio/docs/superpowers/specs/2026-11-20-object-sorting-design.md)

## Global Constraints

- Keep all sorting logic inside `OpenBISLabelStudio.Backend`; `Form1` must only reference `OpenBISLabelStudio.Backend.Contracts` types (`SampleSortField`, `SampleSortDirection`, `SampleSummaryDto`, `ILabelStudioBackend`), never `OpenBISLabelStudio.Backend.Services.SampleSorter` directly (`FrontendFormsContainNoBackendServiceOrSecretReferences`).
- Never re-query openBIS or Python (`openbis_fetch.py`) when the user changes sort field or direction.
- Preserve the currently selected item's `PermId` across re-sorts (falling back to index `0` only if missing or empty); keep chosen sort field and direction sticky across object-type switches during a session, with no persistence to `config/`.
- Never commit `config/` files, real URLs/tokens, or break existing Debug/Release MSBuild builds or `pytest` tests.

## Review Focus

1. **Multi-digit / 20+ digit numeric tokens & leading zeros in `Name`:** Natural numeric comparison must order `0a`, `2a`, `10a`, `100a`, `751a`, handle leading zeros cleanly without overflowing `Int64`, and ignore case consistently (`a2` before `A10`).
2. **Duplicate names & multi-length `PermId` sequence numbers:** Entries sharing `Name` (such as two `"0a"` samples) or differing `PermId` suffix lengths (`20260924152631220-8238` vs `20260924152631220-10170`) or malformed/non-numeric PermIDs must sort deterministically without throwing.
3. **Null, empty, unparseable, or offset `ModificationDate` values:** Mixed UTC (`Z`) and offset (`+02:00`) timestamps must compare chronologically; null, blank, or unparseable dates must sort predictably after valid dates without throwing.
4. **Empty lists, in-flight async type switches, and zero-network re-sorts:** Clicking sort or switching back to placeholder `"Select an object type..."` when 0 items are loaded must not crash or fire `LoadObjectsForTypeAsync`; sort controls must disable/enable alongside `openbisObjectsComboBox` in `SetSampleQueryControlsEnabled`.
5. **WinForms row 2 bounds & layout isolation:** `openbisObjectsComboBox` (`605` wide), `sortByLabel`, `sortByComboBox`, and `btnSortDirection` must stay inside `ClientSize (1080, 530)` and not intersect each other, `openbisTypeComboBox`, `lblStatusNotice`, or `prgLoading`.

---

### Task 1: Backend Sort Contracts, `SampleSorter`, and `ILabelStudioBackend.SortSamples`

**Files:**
- Modify: `OpenBISLabelStudio.Backend/Contracts/BackendContracts.cs` (add `SampleSortField`, `SampleSortDirection`, and `ILabelStudioBackend.SortSamples`)
- Create: `OpenBISLabelStudio.Backend/Services/SampleSorter.cs`
- Modify: `OpenBISLabelStudio.Backend/Services/LabelStudioBackend.cs` (implement `SortSamples` and sort `LoadObjectsForTypeAsync` results consistently via `SampleSorter`)
- Test: `OpenBISLabelStudio.Tests/Program.cs` (update `StubLabelStudioBackend` + add unit tests for `SampleSorter` and `LabelStudioBackend.SortSamples`)

**Interfaces:**
- Produces (consumed by Task 2):
  - `OpenBISLabelStudio.Backend.Contracts.SampleSortField` (`ModificationDate`, `Name`, `PermId`)
  - `OpenBISLabelStudio.Backend.Contracts.SampleSortDirection` (`Ascending`, `Descending`)
  - `IReadOnlyList<SampleSummaryDto> ILabelStudioBackend.SortSamples(IReadOnlyList<SampleSummaryDto>? samples, SampleSortField sortBy, SampleSortDirection direction)`

- [ ] **Step 1: Write the failing backend sorting tests and update `StubLabelStudioBackend` in `OpenBISLabelStudio.Tests/Program.cs`**

Register three new test methods in `Main` (`BackendSampleSorterOrdersByNamePermIdAndModificationDate`, `BackendSampleSorterHandlesEdgeCasesWithoutThrowing`, `BackendSortSamplesIsolatesInputAndBreaksDuplicateTies`), add `public int SortSamplesCalls { get; private set; }` plus `public IReadOnlyList<SampleSummaryDto> SortSamples(IReadOnlyList<SampleSummaryDto>? samples, SampleSortField sortBy, SampleSortDirection direction) { SortSamplesCalls++; return SampleSorter.Sort(samples, sortBy, direction); }` to `StubLabelStudioBackend`, and write tests covering:
  - Natural alphanumeric `Name` ordering (`Ascending` and `Descending`) for `["751a", "0a", "100a", "10a", "2a", "a2", "A10", "000000000000000000002b", "0000000000000000000010b"]` -> expects `0a, 2a, 10a, 100a, 751a, a2, A10, 000000000000000000002b, 0000000000000000000010b` in `Ascending`, and exact reverse in `Descending`.
  - `PermId` ordering (`Ascending` and `Descending`) for `20260924152631220-10170`, `20260924152631220-8238`, `20251113162300874-4172`, `"ABC-10"`, `"ABC-2"`, `""`, verifying `-8238` precedes `-10170` in `Ascending` and follows it in `Descending`.
  - `ModificationDate` ordering with mixed UTC (`2026-10-14T09:00:00Z`), offset (`2026-10-14T10:30:00+02:00`, equivalent to `08:30:00Z`), blank `""`, `null`, and `"not-a-date"`: valid timestamps sort chronologically in both `Ascending` and `Descending`, while blank/unparseable dates always fall after valid dates without throwing.
  - Duplicate names (`0a` with `20251113162300874-4172` and `0a` with `20260924152631220-8238`) break ties deterministically by `PermId` according to the active direction; `null` list or `null` elements inside the list return cleanly without mutating caller lists; `LabelStudioBackend.SortSamples` delegates identically.

- [ ] **Step 2: Run build/test to verify it fails**

Run: `& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" OpenBISLabelStudio.slnx /t:Build /p:Configuration=Debug /v:minimal`
Expected: FAIL with missing `SampleSortField`, `SampleSortDirection`, and `SampleSorter` compile errors.

- [ ] **Step 3: Add `SampleSortField`, `SampleSortDirection`, and `SortSamples` to `OpenBISLabelStudio.Backend/Contracts/BackendContracts.cs`**

Define:
```csharp
public enum SampleSortField
{
    ModificationDate = 0,
    Name = 1,
    PermId = 2
}

public enum SampleSortDirection
{
    Ascending = 0,
    Descending = 1
}
```
and add to `ILabelStudioBackend`:
```csharp
IReadOnlyList<SampleSummaryDto> SortSamples(
    IReadOnlyList<SampleSummaryDto>? samples,
    SampleSortField sortBy,
    SampleSortDirection direction);
```

- [ ] **Step 4: Implement `SampleSorter` in `OpenBISLabelStudio.Backend/Services/SampleSorter.cs` and wire `LabelStudioBackend`**

- `public static class SampleSorter` with `public static IReadOnlyList<SampleSummaryDto> Sort(IReadOnlyList<SampleSummaryDto>? samples, SampleSortField sortBy, SampleSortDirection direction)` returning a new `.AsReadOnly()` list with null items skipped.
- Implement `CompareNatural(string? a, string? b)` scanning contiguous ASCII digit (`char.IsAsciiDigit`) vs non-digit runs without regex/overflow: compare stripped leading-zero digit string lengths first, then `string.CompareOrdinal` on the stripped digit slices, then non-digit runs with `StringComparer.CurrentCultureIgnoreCase` -> `OrdinalIgnoreCase` -> `Ordinal`. For `Name`, empty/whitespace strings sort after non-empty names in both directions so missing names never float to index 0; if all chunks tie semantically, do a deterministic raw-string tie-break (`OrdinalIgnoreCase`, then `Ordinal`).
- Implement `ModificationDate` comparison via `DateTimeOffset.TryParse(..., CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset parsed)`: valid dates always sort before blank/unparseable dates regardless of direction (`if (hasA != hasB) return hasA ? -1 : 1;`), with valid dates flipped by `direction`, and two unparseable non-empty strings compared case-insensitively before PermID tie-breaking.
- Break equal primary comparisons with `CompareNatural(a.PermId, b.PermId)` (with blank PermIDs after non-blank, flipped by `direction`).
- In `LabelStudioBackend`: implement `SortSamples(...) => SampleSorter.Sort(samples, sortBy, direction);` and wrap `LoadObjectsForTypeAsync`'s returned `samples` through `SampleSorter.Sort(samples, SampleSortField.ModificationDate, SampleSortDirection.Descending)` so default loads share exact tie-breaking rules.

- [ ] **Step 5: Build Debug & run backend tests to verify they pass**

Run:
`& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" OpenBISLabelStudio.slnx /t:Restore,Build /p:Configuration=Debug /v:minimal`
`.\OpenBISLabelStudio.Tests\bin\Debug\net10.0-windows\OpenBISLabelStudio.Tests.exe`
Expected: All existing tests plus the 3 new backend sort tests report `[PASS]` with `0 failed`.

- [ ] **Step 6: Commit Task 1**

```bash
git add OpenBISLabelStudio.Backend/Contracts/BackendContracts.cs OpenBISLabelStudio.Backend/Services/SampleSorter.cs OpenBISLabelStudio.Backend/Services/LabelStudioBackend.cs OpenBISLabelStudio.Tests/Program.cs
git commit -m "feat: add backend SampleSorter and ILabelStudioBackend.SortSamples"
```

---

### Task 2: `Form1` Sort Controls, Selection Preservation, and UI Integration Tests

**Files:**
- Modify: `OpenBISLabelStudio/Form1.Designer.cs` (narrow `openbisObjectsComboBox` to `605`; add `sortByLabel`, `sortByComboBox`, and `btnSortDirection`)
- Modify: `OpenBISLabelStudio/Form1.cs` (initialize sort options, store `_loadedTypeSamples`, implement `ApplySortedSamples`, handle `sortByComboBox_SelectedIndexChanged` & `btnSortDirection_Click`, update `SetSampleQueryControlsEnabled` and placeholder/reset clears)
- Test: `OpenBISLabelStudio.Tests/Program.cs` (extend `Form1HasSettingsButtonWithoutOverlaps` for row 2 sort controls and add `Form1SortControlsReorderLoadedObjectsAndPreserveSelectionWithoutNetworkCalls`)

**Interfaces:**
- Consumes from Task 1:
  - `OpenBISLabelStudio.Backend.Contracts.SampleSortField`
  - `OpenBISLabelStudio.Backend.Contracts.SampleSortDirection`
  - `ILabelStudioBackend.SortSamples(IReadOnlyList<SampleSummaryDto>? samples, SampleSortField sortBy, SampleSortDirection direction)`

- [ ] **Step 1: Write the failing Form1 layout and interaction tests in `OpenBISLabelStudio.Tests/Program.cs`**

Register `Form1SortControlsReorderLoadedObjectsAndPreserveSelectionWithoutNetworkCalls` in `Main`, and update `Form1HasSettingsButtonWithoutOverlaps` plus add the new integration test verifying:
  - Row 2 layout & non-overlap: locate `openbisObjectsComboBox`, `sortByLabel` (`"Sort:"`), `sortByComboBox` (`DropDownStyle == ComboBoxStyle.DropDownList`), and `btnSortDirection` (`"\u2193"` / `"↓"`); verify all are inside `form.ClientSize`, none intersect each other, `openbisTypeComboBox`, `lblStatusNotice`, `prgLoading`, or the three label `GroupBox`es, and their right edge matches `openbisTypeComboBox.Right` (`1040`).
  - Option 1 defaults & interaction with `StubLabelStudioBackend`: load `SUBSTANCE` samples `["0a (2025...-4172)", "100a (...)", "2a (...)", "0a (2026...-8238)", "10a (...)"]`; verify `sortByComboBox` starts on `Modification Date` with `btnSortDirection.Text == "↓"` (`stub.SortSamplesCalls` increments cleanly when sorting), and selecting `"2a (...)"` then switching `sortByComboBox` to `Name` switches `btnSortDirection.Text` to `"↑"`, reorders `openbisObjectsComboBox.Items` to `0a (2025...)`, `0a (2026...)`, `2a`, `10a`, `100a`, keeps `"2a (...)"` selected, and performs **0 extra `LoadObjectsCalls`**.
  - Clicking `btnSortDirection` reverses order (`"↓"`, `100a, 10a, 2a, 0a (2026...), 0a (2025...)`) while keeping `"2a (...)"` selected.
  - Switching `sortByComboBox` to `PermID` defaults `btnSortDirection.Text` to `"↓"` (newest PermID first), keeps `"2a (...)"` selected, and switching to `SUBSTRATE` retains `PermID` `↓` and applies it to the newly loaded items without errors.
  - Selecting `PLACEHOLDER_TYPE` clears `openbisObjectsComboBox.Items` safely with zero exceptions even when toggling `btnSortDirection` on an empty list.

- [ ] **Step 2: Run build/test to verify it fails**

Run:
`& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" OpenBISLabelStudio.slnx /t:Build /p:Configuration=Debug /v:minimal`
`.\OpenBISLabelStudio.Tests\bin\Debug\net10.0-windows\OpenBISLabelStudio.Tests.exe`
Expected: FAIL because `sortByLabel`, `sortByComboBox`, and `btnSortDirection` do not exist yet on `Form1`.

- [ ] **Step 3: Update `OpenBISLabelStudio/Form1.Designer.cs` with Option 1 row 2 controls**

Keep existing tab order and row alignment cleanly:
- Change `openbisObjectsComboBox.Size` from `new Size(875, 28)` to `new Size(605, 28)` at `Location(165, 62)`.
- Add `sortByLabel` (`AutoSize = true`, `Location(782, 65)`, `Text = "Sort:"`), `sortByComboBox` (`DropDownStyle = ComboBoxStyle.DropDownList`, `FormattingEnabled = true`, `Location(828, 62)`, `Size(166, 28)`), and `btnSortDirection` (`Location(1002, 62)`, `Size(38, 28)`, `Text = "\u2193"`, `UseVisualStyleBackColor = true`, `AccessibleName = "Sort Direction"`). Adjust `TabIndex` values for controls after row 2 cleanly or place the new controls cleanly in `InitializeComponent()`.

- [ ] **Step 4: Implement sort state, events, and selection preservation in `OpenBISLabelStudio/Form1.cs`**

- Add private `SortFieldOption` helper class wrapping `(SampleSortField Field, string DisplayText)` and fields:
  - `private IReadOnlyList<SampleSummaryDto> _loadedTypeSamples = Array.Empty<SampleSummaryDto>();`
  - `private SampleSortField _currentSortField = SampleSortField.ModificationDate;`
  - `private SampleSortDirection _currentSortDirection = SampleSortDirection.Descending;`
  - `private bool _isUpdatingSortControls;`
- Populate `sortByComboBox` in the constructor after `InitializeComponent()` with `Modification Date`, `Name`, `PermID`, select `Modification Date`, and sync `btnSortDirection` text/tooltip (`↓` / `↑` with readable tooltip text describing active field direction).
- In `openbisTypeComboBox_SelectedIndexChanged` (and Settings credential-reset branch at lines 1236–1247), clear `_loadedTypeSamples = Array.Empty<SampleSummaryDto>();` whenever clearing `openbisObjectsComboBox`, and upon successful `LoadObjectsForTypeAsync`, store `_loadedTypeSamples = result.Samples;` and call `ApplySortedObjects(preserveSelection: false);`.
- In `ApplySortedObjects(bool preserveSelection)`: capture `string? selectedPermId = preserveSelection && openbisObjectsComboBox.SelectedItem is SampleSummaryDto current ? current.PermId : null;`, compute `_backend.SortSamples(_loadedTypeSamples, _currentSortField, _currentSortDirection)`, repopulate `openbisObjectsComboBox.Items` inside `BeginUpdate()` / `EndUpdate()`, restore matching `PermId` (or `SelectedIndex = 0` when `Count > 0` and no match exists).
- Handle `sortByComboBox_SelectedIndexChanged` (guard `_isUpdatingSortControls`, update `_currentSortField`, set `_currentSortDirection = _currentSortField == SampleSortField.Name ? SampleSortDirection.Ascending : SampleSortDirection.Descending`, update direction button text/tooltip, call `ApplySortedObjects(preserveSelection: true)`) and `btnSortDirection_Click` (flip `_currentSortDirection`, update button text/tooltip, call `ApplySortedObjects(preserveSelection: true)`).
- Update `SetSampleQueryControlsEnabled(bool enabled)` to enable/disable `sortByComboBox` and `btnSortDirection` alongside `openbisObjectsComboBox`.

- [ ] **Step 5: Run full verification (Debug build + Release build + unit tests + Python tests)**

Run:
```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild.exe" OpenBISLabelStudio.slnx /t:Restore,Build /p:Configuration=Debug /v:minimal
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild.exe" OpenBISLabelStudio.slnx /t:Restore,Build /p:Configuration=Release /v:minimal
.\OpenBISLabelStudio.Tests\bin\Debug\net10.0-windows\OpenBISLabelStudio.Tests.exe
.\OpenBISLabelStudio.Tests\bin\Release\net10.0-windows\OpenBISLabelStudio.Tests.exe
pytest tests/test_openbis_fetch.py -v
```
Expected: MSBuild Debug & Release succeed (`exit 0`), all tests pass in both Debug and Release (`0 failed`), `pytest` passes (`21 passed`), and `git status` confirms no `config/` files or credentials are tracked/modified.

- [ ] **Step 6: Commit Task 2**

```bash
git add OpenBISLabelStudio/Form1.Designer.cs OpenBISLabelStudio/Form1.cs OpenBISLabelStudio.Tests/Program.cs
git commit -m "feat: add Option 1 object sort dropdown and direction toggle to Form1"
```
