# Design: Strict Backend / Frontend Separation (`OpenBISLabelStudio.Backend`)

**Date:** 2026-11-20  
**Branch:** `feat/first-run-config`  
**Solution:** `OpenBISLabelStudio.slnx`  
**Work Size:** Project (multi-project solution split + thin UI refactor + persistent Python worker)

---

## 1. Intent & Why

Although helper classes (`AppConfigService`, `OpenBisClient`, `LabelPreviewRenderer`) and `openbis_fetch.py` already exist, `Form1.cs` and `SetupSettingsForm.cs` still hold substantial backend logic directly: raw openBIS URL and Personal Access Token fields, expired-token retry and recovery orchestration (`ExecuteWithExpiredTokenRecoveryAsync`, `TokenRecoveryCoordinator`), openBIS type merging/caching, config file persistence under `config/`, `--skip-eln` template decisions, Brother PT-P700 filtering, and direct `OpenBisClient` / `LabelPreviewRenderer` calls.

This refactor establishes a compiler-enforced boundary between **Backend** (`OpenBISLabelStudio.Backend`) and **Frontend** (`OpenBISLabelStudio`) while both continue to run on the same Windows x64 machine and publish into a single deployable folder:

- **Backend owns all state, external integrations, and business rules:** openBIS sessions and queries (`pybis`), expired-token retry coordination, configuration files under `config/`, template profile rules, label rendering/caching, printer discovery/filtering/validation, and Brother b-PAC printing.
- **Frontend owns only presentation:** rendering WinForms controls, loading indicators, user input events, status/error dialogs, and forwarding user actions to a clean backend interface (`ILabelStudioBackend`).

---

## 2. Goals, Non-Goals, and Anti-Goals

### Goals
1. **Separate Backend Project (`OpenBISLabelStudio.Backend`):**
   - Add a dedicated class library project (`OpenBISLabelStudio.Backend.csproj`, `net10.0-windows`, `x64`, `UseWindowsForms=true` strictly for `System.Drawing` / `PrinterSettings` SDK availability without adding NuGet dependencies) in `OpenBISLabelStudio.slnx`.
   - Move `Models/`, `Services/`, `openbis_fetch.py`, `templates/`, `<PackageReference Include="Newtonsoft.Json" />`, and `<COMReference Include="bpac" />` into `OpenBISLabelStudio.Backend`.
2. **Backend Facade (`ILabelStudioBackend` / `LabelStudioBackend`):**
   - Expose a clean, UI-agnostic contract using DTOs/ViewModels (including PNG `byte[]` preview images so WinForms UI never shares GDI+ object lifetimes or COM/b-PAC references with backend internals).
   - Move all openBIS credential handling, token retry/recovery coordination, type description merging, `--skip-eln` selection, example preview caching, and print-job construction out of `Form1.cs` and `SetupSettingsForm.cs` into `LabelStudioBackend`.
3. **Thin WinForms Frontend (`OpenBISLabelStudio`):**
   - `OpenBISLabelStudio.csproj` references **only** `OpenBISLabelStudio.Backend` (no direct `Newtonsoft.Json` package reference, no `bpac` COM reference, and no `openbis_fetch.py` / `AppConfigService` / raw config path access).
   - `Form1` and `SetupSettingsForm` receive `ILabelStudioBackend` (or a `LabelStudioBackend` factory), track UI cancellation tokens/request versions, display progress bars (`prgLoading`, `prgTypesLoading`, `prgConnectionLoading`), and convert returned PNG `byte[]` bytes into `Bitmap`s for `PictureBox` controls.
   - Because `bpac.Document` has empty COM `ThreadingModel`, `OpenBISLabelStudio.exe --preview-worker` remains the executable host entry point, immediately delegating to `LabelStudioBackend.RunPreviewWorkerLoop(Console.In, Console.Out)` in 4 lines of `Program.cs`.
4. **Optional Fast Persistent openBIS Worker (`openbis_fetch.py --worker`):**
   - Keep `pybis.Openbis` warm in a persistent child stdio worker managed by `OpenBisClient` inside the backend so type switches and `--details` calls avoid paying the ~2.1s `import pybis` startup cost repeatedly, while keeping single-shot CLI (`--check`, `--types`, `--details`, `<TYPE>`) intact for standalone CLI/tests and stub fallback.
5. **Preserve Single-Folder Publish & Existing Behavior:**
   - MSBuild Release `/t:Publish` and Debug/Release builds continue copying `openbis_fetch.py` and `templates/*.lbx` into the application output without leaking `config/`.
   - All existing unit/integration coverage (migrated and expanded in `OpenBISLabelStudio.Tests` and `tests/test_openbis_fetch.py`) stays green.

### Non-Goals
- Running the backend over HTTP/TCP on a remote server (both frontend and backend stay on the same Windows PC).
- Replacing WinForms (`Form1` / `SetupSettingsForm`) with WPF, Blazor, or a web UI (though `ILabelStudioBackend` makes that easier later).
- Changing `.lbx` template designs, JSON schemas under `config/`, or Brother PT-P700 print options.

### Anti-Goals
- Never allow `OpenBISLabelStudio.Backend` to reference `System.Windows.Forms.Form`, `MessageBox`, `Control`, or show dialogs directly.
- Never allow `Form1.cs` or `SetupSettingsForm.cs` to store openBIS tokens/configs outside explicit Settings connection editing, construct Python CLI arguments, inspect `AppConfigService` file paths, or import `bpac`.
- Never allow `dotnet build` workarounds or break `MSBuild.exe OpenBISLabelStudio.slnx /t:Restore,Build` or `/t:Publish`.

---

## 3. Architecture & Boundaries

### 3.1 Solution & Project Dependencies

```text
OpenBISLabelStudio.slnx
├── OpenBISLabelStudio (WinExe, net10.0-windows, x64)
│   ├── Program.cs                  # Minimal host entrypoint + --preview-worker passthrough
│   ├── Form1.cs / .Designer.cs     # Pure presentation (dropdowns, previews, loading bar, Print click)
│   └── SetupSettingsForm.cs / .Designer.cs  # Wizard/Settings/Recovery UI calling ILabelStudioBackend
│       └── ProjectReference ──► OpenBISLabelStudio.Backend
│
├── OpenBISLabelStudio.Backend (Library, net10.0-windows, x64)
│   ├── Contracts/                  # ILabelStudioBackend, ITokenRecoveryPrompter, facade DTOs
│   ├── Models/                     # ConfigModels.cs (internal/domain DTOs & enums)
│   ├── Services/                   # LabelStudioBackend, AppConfigService, OpenBisClient, LabelPreviewRenderer, TokenRecoveryCoordinator
│   ├── openbis_fetch.py            # Copied to output via PreserveNewest
│   └── templates/*.lbx             # Copied to output/templates/ via PreserveNewest
│
├── OpenBISLabelStudio.Tests        # Tests both OpenBISLabelStudio.Backend and thin WinForms forms
└── tests/test_openbis_fetch.py     # Imports openbis_fetch.py from OpenBISLabelStudio.Backend/
```

**No-UI-types rule inside Backend:** Although `OpenBISLabelStudio.Backend` uses `net10.0-windows` (for `System.Drawing` / `PrinterSettings`), backend code must never reference `Form`, `MessageBox`, `Control`, `ProgressBar`, or any UI type. Expired-token prompting crosses the boundary strictly through an injected callback (`ITokenRecoveryPrompter` or `Func<TokenRecoveryRequest, Task<bool>>`).

### 3.2 `ILabelStudioBackend` Facade Contract

All frontend communication flows through `ILabelStudioBackend` (with clean request/response models; preview images cross the boundary as `byte[]` PNG payloads plus explicit status flags):

- **Startup & Lifecycle:**
  - `void WarmUpInBackground()` — pre-warms `--preview-worker` (and optionally Python openBIS worker when valid credentials exist).
  - `Task<WorkspaceStartupState> InitializeWorkspaceAsync(ITokenRecoveryPrompter? recoveryPrompter, CancellationToken ct)` — inspects `config/`, reports missing wizard step if setup is incomplete (`MissingWizardStep`), returns sorted display options for configured object types (`IReadOnlyList<TypeOptionDto>` containing `Code`, `DisplayText`, `IsMoleculeProfile`), installed/fallback `Brother PT-P700` printer dropdown state (`PrinterDropdownState`), and offline fallback status if needed.
  - `Task<BackgroundTypeRefreshResult> RefreshWorkspaceTypesAsync(ITokenRecoveryPrompter? recoveryPrompter, CancellationToken ct)` — refreshes openBIS descriptions, updates `config/openbis_types.json` when changed, handles expired-token recovery & retries internally, and returns updated `IReadOnlyList<TypeOptionDto>` + non-fatal banner notice if offline.
- **Wizard & Settings Flows:**
  - `Task<ConnectionCheckResult> TestConnectionAsync(string url, string token, bool persistOnSuccess, CancellationToken ct)` — runs fast `--check` (`isSessionActive`), optionally saves `config/openbis_config.json`, returns status + whether token input should be focused on auth failure.
  - `Task<SettingsSnapshotDto> GetSettingsSnapshotAsync(CancellationToken ct)` — returns current connection fields (for Settings Connection tab & Wizard prefill), saved/merged `ObjectTypeRowDto` items, printer dropdown items + warning state, and whether initial types+printers already exist.
  - `Task<ServerTypeSyncResult> FetchAndMergeServerTypesAsync(string url, string token, IReadOnlyList<ObjectTypeRowDto> currentRows, CancellationToken ct)` — queries `--types`, merges via existing `BuildInitialTypeRows` rules while preserving selection/profile edits, classifies `token_expired` vs offline fallback warning.
  - `Task<Dictionary<LabelTemplateType, byte[]>> GetExamplePreviewsAsync(LabelTemplateProfile profile, CancellationToken ct)` — serves cached/rendered example slot PNG bytes (returns per-slot fallback PNG bytes on error, never raw `bpac` errors).
  - `Task<OperationResult> SaveSettingsOfflineAsync(SettingsSaveRequest request, CancellationToken ct)` & `Task<OperationResult> SaveWizardStepAsync(...)` — validates URL/token/types/printers and writes `config/*.json` atomically.
- **Main Screen Sample, Preview & Printing Flows:**
  - `Task<SampleListResultDto> LoadObjectsForTypeAsync(string typeCode, ITokenRecoveryPrompter? recoveryPrompter, CancellationToken ct)` — queries samples for `typeCode` with transparent expired-token retry and returns `{Name, PermId}` list items.
  - `Task<SamplePreviewBundleDto> LoadSamplePreviewsAsync(string typeCode, string permId, string magnetFreeText, string flaskFreeText, ITokenRecoveryPrompter? recoveryPrompter, CancellationToken ct)` — determines `LabelTemplateProfile` and `skipElnPreview`, fetches sample details, stores active sample state cleanly or returns an opaque session handle / snapshot ID alongside rendered PNG `byte[]` previews per slot (or per-slot streaming callback `Action<LabelTemplateType, byte[]>` so Small Box / Magnet / Flask update each `PictureBox` progressively as soon as each slot finishes!).
  - `Task<byte[]> RenderSlotWithFreeTextAsync(LabelTemplateType slot, string freeText, CancellationToken ct)` — re-renders Magnet or Flask when Free Text changes using the backend's active loaded sample without the frontend holding raw `SampleDetailsDto`.
  - `Task<OperationResult> UpdatePrinterSelectionsAsync(string smallBoxPrinter, string magnetPrinter, string flaskPrinter)` — persists `config/printers_config.json` on dropdown change.
  - `Task<LabelPrintBatchResult> PrintSelectedLabelsAsync(IReadOnlyList<PrintSlotSelection> selections, CancellationToken ct)` — validates checked slots/printers and dispatches printing through b-PAC worker using the active loaded sample.

*(Note: Progressive per-slot reporting via `Func<LabelTemplateType, byte[], Task>` or `IProgress<(LabelTemplateType Slot, byte[] PngBytes)>` preserves today's progressive UX where each preview box flips from `"Rendering label preview..."` to the finished label immediately rather than waiting for all 3 slots to complete.)*

### 3.3 Expired-Token Recovery Decoupling

1. When any backend openBIS call sees `token_expired`:
   - Backend checks whether persisted credentials changed while the call was in flight; if so, retries automatically.
   - Otherwise, `TokenRecoveryCoordinator` inside the backend coalesces concurrent triggers and calls `recoveryPrompter.PromptForNewTokenAsync(url, warningMessage)`.
2. The frontend implementation of `ITokenRecoveryPrompter` opens `SetupSettingsForm` in `SetupDialogMode.ExpiredTokenRecovery` on the UI thread, where **Save Token** calls `backend.TestConnectionAsync(url, newToken, persistOnSuccess: true)`.
3. If saved (`true`), the backend reloads the new credentials and retries the original openBIS call transparently. If canceled (`false`), the backend returns cleanly with a clear status notice for the UI banner.

### 3.4 Python Worker (`openbis_fetch.py --worker`)

- Keep all existing CLI commands (`--check`, `--types`, `<TYPE>`, `--details <perm_id> [--skip-eln]`) intact so unit tests and direct invocations remain 100% compatible.
- Add an optional `--worker` mode in `openbis_fetch.py`: reads single-line JSON requests on `stdin`, caches the active `pybis.Openbis` instance when `(url, token)` matches (avoiding the ~2.1s `import pybis` overhead and reusing `Openbis.cache` after the first query), executes the same `check_connection` / `fetch_sample_types` / `fetch_samples_by_type` / `fetch_sample_details` functions, and writes single-line JSON responses on `stdout`.
- `OpenBisClient` spawns `--worker` lazily only when a real script (supporting `--worker` or enabled by default in production) is present, falls back automatically to one-shot CLI if a test uses a minimal stub `openbis_fetch.py` or invalid/missing `python`, and shuts the Python worker down on `Dispose()` / `ProcessExit`.

---

## 4. Verification & Testing Strategy

1. **Test Project References Both Backend & Frontend:**
   - All existing `AppConfigService`, `OpenBisClient`, `LabelPreviewRenderer`, `TokenRecoveryCoordinator`, and WinForms `SetupSettingsForm` / `Form1` tests in `OpenBISLabelStudio.Tests/Program.cs` stay green.
   - Add dedicated backend facade tests (`LabelStudioBackend`) covering:
     - Clean separation (backend handles expired-token coalescing + retry via mock `ITokenRecoveryPrompter` without UI forms).
     - Main-form sample loading + progressive slot preview PNG bytes + Free Text re-render + printer auto-save + missing-printer print rejection.
     - Thin `Form1` / `SetupSettingsForm` UI behavior verified against a stubbed/mock `ILabelStudioBackend`.
   - Update `tests/test_openbis_fetch.py` `sys.path` to point at `OpenBISLabelStudio.Backend/` and add tests for `--worker` mode.
2. **Build, Publish & Hygiene Verification:**
   - `MSBuild.exe OpenBISLabelStudio.slnx /t:Restore,Build` succeeds in Debug and Release with 0 warnings/errors.
   - `MSBuild.exe OpenBISLabelStudio\OpenBISLabelStudio.csproj /t:Publish /p:Configuration=Release /p:PublishDir="..."` produces a clean runnable folder containing `OpenBISLabelStudio.exe`, `OpenBISLabelStudio.Backend.dll`, `Newtonsoft.Json.dll`, `openbis_fetch.py`, and `templates/*.lbx` (and zero `config/` leaks).
   - Verify no `<COMReference Include="bpac">` or `Newtonsoft.Json` reference remains in frontend `OpenBISLabelStudio.csproj`.
