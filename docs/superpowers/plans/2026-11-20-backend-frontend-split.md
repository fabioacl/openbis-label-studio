# Backend / Frontend Separation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Refactor `openbis-label-studio` into a compiler-enforced **Backend** (`OpenBISLabelStudio.Backend` class library) and **Frontend** (`OpenBISLabelStudio` thin WinForms UI) running on the same Windows x64 machine with an optional persistent `openbis_fetch.py --worker` stdio session, zero secret leakage in Git/publish outputs, and preserved single-folder MSBuild Release deployment.

**Architecture:** Extract all domain models, configuration I/O, openBIS client/session recovery, and b-PAC label rendering/printing into `OpenBISLabelStudio.Backend` behind an `ILabelStudioBackend` facade returning UI-ready DTOs and PNG `byte[]` payloads. Keep `OpenBISLabelStudio` (`Form1`, `SetupSettingsForm`, `Program.cs`) purely focused on presentation, cancellation/request-version tracking, progress indicators, and opening `SetupSettingsForm` via `ITokenRecoveryPrompter`. Keep `openbis_fetch.py` one-shot CLI compatible while adding `--worker` stdio session reuse for faster repeat openBIS calls.

**Tech Stack:** C# / .NET 10 (`net10.0-windows`, `x64`), WinForms, `Newtonsoft.Json` 13.0.4, Brother `bpac` 1.3 COM reference, Python 3 + `pybis` + `requests` + `pytest`, MSBuild (`VS 18 Community`).

**Spec:** `docs/superpowers/specs/2026-11-20-backend-frontend-split-design.md`

## Global Constraints

- Both projects target `<TargetFramework>net10.0-windows</TargetFramework>` and `<PlatformTarget>x64</PlatformTarget>`; always build and publish with `"C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"` (`dotnet build` fails `MSB4803` on the `bpac` COM reference).
- `OpenBISLabelStudio.Backend` uses `<UseWindowsForms>true</UseWindowsForms>` solely for `System.Drawing` / `PrinterSettings`, and MUST contain zero references to `System.Windows.Forms.Form`, `MessageBox`, `Control`, `ProgressBar`, `PictureBox`, or `Application`.
- Frontend `OpenBISLabelStudio.csproj` MUST reference ONLY `..\OpenBISLabelStudio.Backend\OpenBISLabelStudio.Backend.csproj` — no `<COMReference Include="bpac">`, no `<PackageReference Include="Newtonsoft.Json" />`, and no direct access to `openbis_fetch.py`, `templates\*.lbx`, or `config\` paths outside backend facade contracts.
- Preview rendering in `LabelPreviewRenderer` must preserve `"Microsoft Print to PDF"` override when Brother drivers are absent (`OneNote (Desktop)` clips labels), while UI printer dropdowns strictly filter to `Brother PT-P700` queues (`FilterBrotherPtP700Printers` / `ResolveBrotherPrinterDropdownItems`).
- Zero openBIS tokens, institution URLs, or `config/` files (`openbis_config.json`, `openbis_types.json`, `printers_config.json`) may be committed, tracked, or included in `/t:Publish` outputs; keep `Condition="Exists('config')"` off tracked content or ensure `config/` does not exist in project source directories during publish.
- Preserve existing `parse_cli_args(...) -> tuple[str, str, str, str | None, str]` signature and lazy `Openbis` loader in `openbis_fetch.py`, and keep `OpenBISLabelStudio.exe --preview-worker` host entry point in `Program.cs` delegating to `LabelStudioBackend.RunPreviewWorkerLoop(Console.In, Console.Out)`.

## Review Focus

- **Corrupt stdout or unexpected EOF mid-session in `openbis_fetch.py --worker`:** `OpenBisClient` must not wedge or leak broken pipes; it tears down the crashed worker, surfaces `"unknown_error"` or falls back cleanly to one-shot CLI if `--worker` is unsupported by a minimal stub script. (Pinned in Task 2.)
- **Simultaneous expired-token responses or canceled/faulted `ITokenRecoveryPrompter`:** Concurrent calls must coalesce to a single recovery prompt via `TokenRecoveryCoordinator`, short-circuit retry if credentials already changed in flight, return `WasCancelled = true` with a clear banner notice when the user cancels, and never wedge future prompts if a prompter throws. (Pinned in Task 3.)
- **Free-Text edits or Print clicks before any sample is loaded (or after switching Object Type):** Changing Object Type must call `ClearActiveSample()`, Free-Text re-render with no active sample must return `Array.Empty<byte>()` safely, and `PrintSelectedLabelsAsync` with no active sample must fail cleanly with `"No active sample loaded. Click 'Load Templates' first."` rather than printing stale sample labels. (Pinned in Task 3.)
- **Corrupt, truncated, or zero-byte PNG payloads crossing the backend boundary:** WinForms UI (`Form1` / `SetupSettingsForm`) must clone decoded PNGs into independent `Bitmap` instances, dispose temporary `MemoryStream`s safely, and fall back gracefully to placeholder text/status without throwing `ArgumentException`. (Pinned in Task 4.)
- **Injected base-directory config isolation and MSBuild Publish secret hygiene:** `LabelStudioBackend(baseDir, ...)` must isolate all `config/*.json` reads/writes strictly under `baseDir`, and MSBuild Release `/t:Publish` must exclude any `config/`, `openbis_config.json`, `openbis_types.json`, or `printers_config.json` files while still packaging `OpenBISLabelStudio.Backend.dll`, `Newtonsoft.Json.dll`, `openbis_fetch.py`, and `templates/*.lbx`. (Pinned in Task 3 & Task 5.)

---

## File Structure

- **New Project `OpenBISLabelStudio.Backend/`:**
  - `OpenBISLabelStudio.Backend.csproj` — Class library (`net10.0-windows`, `x64`, `Newtonsoft.Json` 13.0.4, `bpac` COM ref, copies `openbis_fetch.py` and `templates\**\*.lbx`).
  - `Contracts/BackendContracts.cs` — `ILabelStudioBackend`, `ITokenRecoveryPrompter`, request/response DTOs.
  - `Models/ConfigModels.cs` — Moved from `OpenBISLabelStudio/Models/` (enums, config/openBIS/print domain models).
  - `Services/AppConfigService.cs` — Moved from `OpenBISLabelStudio/Services/` (plus `ValidateConnectionInputs`, `BuildInitialTypeRows`, `ResolveTemplateProfile`, `CredentialsChangedSinceRequest` helpers or hosted cleanly in Backend).
  - `Services/OpenBisClient.cs` — Moved from `OpenBISLabelStudio/Services/` and enhanced with `IDisposable`, `--worker` stdio session reuse, and automatic CLI fallback.
  - `Services/LabelPreviewRenderer.cs` — Moved from `OpenBISLabelStudio/Services/` and extended with `Task<byte[]>` PNG byte rendering/loading/fallback APIs.
  - `Services/TokenRecoveryCoordinator.cs` — Extracted from `Form1.cs` into Backend.
  - `Services/LabelStudioBackend.cs` — `ILabelStudioBackend` implementation owning openBIS workflows, token recovery orchestration, type merging, example/sample preview state & PNG rendering, printer filtering/persistence, and print dispatch.
  - `openbis_fetch.py` — Moved from `OpenBISLabelStudio/` and extended with `--worker` stdio loop + session cache.
  - `templates/*.lbx` — Moved from `OpenBISLabelStudio/templates/`.
- **Thin Frontend `OpenBISLabelStudio/`:**
  - `OpenBISLabelStudio.csproj` — References only `OpenBISLabelStudio.Backend.csproj` (removes `bpac`, `Newtonsoft.Json`, Python script, and templates).
  - `Program.cs` — Minimal entry point (`--preview-worker` passthrough + `LabelStudioBackend` lifecycle).
  - `FormTokenRecoveryPrompter.cs` — UI-thread `ITokenRecoveryPrompter` adapter opening `SetupSettingsForm` in `ExpiredTokenRecovery` mode.
  - `Form1.cs` — Thin WinForms UI consuming `ILabelStudioBackend`.
  - `SetupSettingsForm.cs` — Thin WinForms Settings/Wizard/Recovery dialog consuming `ILabelStudioBackend`.
- **Tests & Docs:**
  - `OpenBISLabelStudio.slnx` — Registers `OpenBISLabelStudio.Backend`.
  - `OpenBISLabelStudio.Tests/OpenBISLabelStudio.Tests.csproj` — References both `OpenBISLabelStudio.Backend` and `OpenBISLabelStudio`.
  - `OpenBISLabelStudio.Tests/Program.cs` — Migrated tests + boundary checks + worker tests + backend facade tests + mock-backend UI tests.
  - `tests/test_openbis_fetch.py` — Updated `sys.path` to `OpenBISLabelStudio.Backend/` + `--worker` unit tests.
  - `README.md` — Updated architecture, publish layout, and config security notes.

---

### Task 1: Create `OpenBISLabelStudio.Backend` Project and Migrate Domain Models, Services & Assets

**Files:**
- Create: `OpenBISLabelStudio.Backend/OpenBISLabelStudio.Backend.csproj`
- Create: `OpenBISLabelStudio.Backend/Services/TokenRecoveryCoordinator.cs`
- Move: `OpenBISLabelStudio/Models/ConfigModels.cs` -> `OpenBISLabelStudio.Backend/Models/ConfigModels.cs`
- Move: `OpenBISLabelStudio/Services/AppConfigService.cs` -> `OpenBISLabelStudio.Backend/Services/AppConfigService.cs`
- Move: `OpenBISLabelStudio/Services/OpenBisClient.cs` -> `OpenBISLabelStudio.Backend/Services/OpenBisClient.cs`
- Move: `OpenBISLabelStudio/Services/LabelPreviewRenderer.cs` -> `OpenBISLabelStudio.Backend/Services/LabelPreviewRenderer.cs`
- Move: `OpenBISLabelStudio/openbis_fetch.py` -> `OpenBISLabelStudio.Backend/openbis_fetch.py`
- Move: `OpenBISLabelStudio/templates/*.lbx` -> `OpenBISLabelStudio.Backend/templates/*.lbx`
- Modify: `OpenBISLabelStudio.slnx`
- Modify: `OpenBISLabelStudio/OpenBISLabelStudio.csproj`
- Modify: `OpenBISLabelStudio/Form1.cs:1168-1232`
- Modify: `OpenBISLabelStudio.Tests/OpenBISLabelStudio.Tests.csproj`
- Modify: `tests/test_openbis_fetch.py:8`
- Test: `OpenBISLabelStudio.Tests/Program.cs`

**Interfaces:**
- Consumes: Existing `OpenBISLabelStudio.Models` (`LabelTemplateProfile`, `LabelTemplateType`, `SetupWizardStep`, `AppConfig`, `ConfiguredOpenBisType`, `PrinterConfig`, `OpenBisTypeItem`, `OpenBisConnectionResult`, `OpenBisTypesResult`, `OpenBisTypeInfoDto`, `OpenBisListResult`, `SampleDto`, `OpenBisDetailsResult`, `SampleDetailsDto`, `IconFlagsDto`, `LabelPrintJobRequest`, `LabelPrintJobResult`, `LabelPrintBatchResult`) and `OpenBISLabelStudio.Services` (`AppConfigService`, `OpenBisClient`, `LabelPreviewRenderer`).
- Produces:
  - Compilable `OpenBISLabelStudio.Backend.dll` assembly owning `OpenBISLabelStudio.Models.*` and `OpenBISLabelStudio.Services.*` (including `TokenRecoveryCoordinator`).
  - Thin `OpenBISLabelStudio.csproj` referencing only `..\OpenBISLabelStudio.Backend\OpenBISLabelStudio.Backend.csproj` (zero `<COMReference Include="bpac">`, zero `Newtonsoft.Json` package ref).
  - Updated `tests/test_openbis_fetch.py` importing from `OpenBISLabelStudio.Backend`.

- [ ] **Step 1: Add the structural backend-boundary test in `OpenBISLabelStudio.Tests/Program.cs` (TDD red)**

Add a test entry `(nameof(BackendAssemblyHasNoUiControlDependencies), BackendAssemblyHasNoUiControlDependencies)` and implementation verifying `typeof(AppConfigService).Assembly.GetName().Name == "OpenBISLabelStudio.Backend"` and that its types, fields, and method signatures contain no references to `System.Windows.Forms.Form`, `Control`, `MessageBox`, or `Application`.

```csharp
private static void BackendAssemblyHasNoUiControlDependencies()
{
    Assembly backendAssembly = typeof(AppConfigService).Assembly;
    AssertEqual("OpenBISLabelStudio.Backend", backendAssembly.GetName().Name,
        "AppConfigService must live in OpenBISLabelStudio.Backend assembly");

    static bool IsDisallowedUiType(Type type) =>
        typeof(Control).IsAssignableFrom(type)
        || typeof(Form).IsAssignableFrom(type)
        || string.Equals(type.FullName, "System.Windows.Forms.MessageBox", StringComparison.Ordinal)
        || string.Equals(type.FullName, "System.Windows.Forms.Application", StringComparison.Ordinal);

    foreach (Type t in backendAssembly.GetTypes())
    {
        AssertFalse(IsDisallowedUiType(t), $"Backend type '{t.FullName}' must not reference WinForms UI types");
        foreach (FieldInfo f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
        {
            AssertFalse(IsDisallowedUiType(f.FieldType), $"Backend field '{t.FullName}.{f.Name}' must not reference UI types");
        }
        foreach (MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            AssertFalse(IsDisallowedUiType(m.ReturnType), $"Backend method '{t.FullName}.{m.Name}' return type must not reference UI types");
            foreach (ParameterInfo p in m.GetParameters())
            {
                AssertFalse(IsDisallowedUiType(p.ParameterType), $"Backend method '{t.FullName}.{m.Name}' parameter '{p.Name}' must not reference UI types");
            }
        }
    }
}
```

- [ ] **Step 2: Run the test to confirm it currently fails against the single-project structure**

Run MSBuild Debug build + test executable:
```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" OpenBISLabelStudio.slnx /t:Restore,Build /p:Configuration=Debug /v:minimal
.\OpenBISLabelStudio.Tests\bin\Debug\net10.0-windows\OpenBISLabelStudio.Tests.exe
```
Expected: `[FAIL] BackendAssemblyHasNoUiControlDependencies` (`Expected: OpenBISLabelStudio.Backend, Actual: OpenBISLabelStudio`).

- [ ] **Step 3: Create `OpenBISLabelStudio.Backend` and move files via `git mv`**

1. Create `OpenBISLabelStudio.Backend/OpenBISLabelStudio.Backend.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <UseWindowsForms>true</UseWindowsForms>
    <ImplicitUsings>enable</ImplicitUsings>
    <PlatformTarget>x64</PlatformTarget>
  </PropertyGroup>

  <ItemGroup>
    <COMReference Include="bpac">
      <WrapperTool>tlbimp</WrapperTool>
      <VersionMinor>3</VersionMinor>
      <VersionMajor>1</VersionMajor>
      <Guid>90359d74-b7d9-467f-b938-3883f4cab582</Guid>
      <Lcid>0</Lcid>
      <Isolated>false</Isolated>
      <EmbedInteropTypes>true</EmbedInteropTypes>
    </COMReference>
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Newtonsoft.Json" Version="13.0.4" />
  </ItemGroup>

  <ItemGroup>
    <None Update="openbis_fetch.py">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
    <None Update="templates\**\*.lbx">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>

</Project>
```
(Do NOT add `<None Update="config\**\*">` in `OpenBISLabelStudio.Backend.csproj`; `config/` is runtime-only beside the deployed `.exe`.)

2. Move files with `git mv`:
   - `OpenBISLabelStudio/Models/ConfigModels.cs` -> `OpenBISLabelStudio.Backend/Models/ConfigModels.cs`
   - `OpenBISLabelStudio/Services/AppConfigService.cs` -> `OpenBISLabelStudio.Backend/Services/AppConfigService.cs`
   - `OpenBISLabelStudio/Services/OpenBisClient.cs` -> `OpenBISLabelStudio.Backend/Services/OpenBisClient.cs`
   - `OpenBISLabelStudio/Services/LabelPreviewRenderer.cs` -> `OpenBISLabelStudio.Backend/Services/LabelPreviewRenderer.cs`
   - `OpenBISLabelStudio/openbis_fetch.py` -> `OpenBISLabelStudio.Backend/openbis_fetch.py`
   - All 6 `.lbx` files under `OpenBISLabelStudio/templates/` -> `OpenBISLabelStudio.Backend/templates/`

3. Extract `TokenRecoveryCoordinator` from `OpenBISLabelStudio/Form1.cs:1168-1232` into `OpenBISLabelStudio.Backend/Services/TokenRecoveryCoordinator.cs` under `namespace OpenBISLabelStudio.Services`, and remove that class declaration from `Form1.cs`.

4. Update `OpenBISLabelStudio/OpenBISLabelStudio.csproj` to strip `COMReference bpac`, `PackageReference Newtonsoft.Json`, `openbis_fetch.py`, and `templates\**\*.lbx`, keeping only:
   - WinExe properties (`OutputType=WinExe`, `TargetFramework=net10.0-windows`, `Nullable=enable`, `UseWindowsForms=true`, `ImplicitUsings=enable`, `PlatformTarget=x64`)
   - `<ProjectReference Include="..\OpenBISLabelStudio.Backend\OpenBISLabelStudio.Backend.csproj" />`

5. Update `OpenBISLabelStudio.slnx` to add `<Project Path="OpenBISLabelStudio.Backend/OpenBISLabelStudio.Backend.csproj" />`.

6. Update `OpenBISLabelStudio.Tests/OpenBISLabelStudio.Tests.csproj` to reference both `..\OpenBISLabelStudio.Backend\OpenBISLabelStudio.Backend.csproj` and `..\OpenBISLabelStudio\OpenBISLabelStudio.csproj`, and add `using System.Reflection;` in `OpenBISLabelStudio.Tests/Program.cs`.

7. Update `tests/test_openbis_fetch.py:8` from `"OpenBISLabelStudio"` to `"OpenBISLabelStudio.Backend"`.

- [ ] **Step 4: Run Debug & Release MSBuild builds, C# tests, and Python tests**

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" OpenBISLabelStudio.slnx /t:Restore,Build /p:Configuration=Debug /v:minimal
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" OpenBISLabelStudio.slnx /t:Restore,Build /p:Configuration=Release /v:minimal
.\OpenBISLabelStudio.Tests\bin\Debug\net10.0-windows\OpenBISLabelStudio.Tests.exe
pytest tests/test_openbis_fetch.py -v
```
Expected: Both MSBuild builds exit 0 with 0 warnings/errors; C# test suite prints `Executed 26 tests, 0 failed.`; `pytest` passes all 17 tests; `OpenBISLabelStudio/bin/Debug/net10.0-windows/OpenBISLabelStudio.Backend.dll`, `openbis_fetch.py`, and `templates/*.lbx` exist.

- [ ] **Step 5: Commit Task 1**

```bash
git add OpenBISLabelStudio.slnx OpenBISLabelStudio.Backend/ OpenBISLabelStudio/OpenBISLabelStudio.csproj OpenBISLabelStudio/Form1.cs OpenBISLabelStudio.Tests/ tests/test_openbis_fetch.py
git commit -m "refactor: extract OpenBISLabelStudio.Backend project and migrate core services"
```

---

### Task 2: Add Persistent Python Worker Mode (`openbis_fetch.py --worker`) and `OpenBisClient` Worker Routing + Fallback

**Files:**
- Modify: `OpenBISLabelStudio.Backend/openbis_fetch.py:193-401,437-501`
- Modify: `OpenBISLabelStudio.Backend/Services/OpenBisClient.cs:16-223`
- Test: `tests/test_openbis_fetch.py`
- Test: `OpenBISLabelStudio.Tests/Program.cs`

**Interfaces:**
- Consumes: `OpenBisClient` single-shot `DeserializeOrError<T>`, existing DTOs (`OpenBisConnectionResult`, `OpenBisTypesResult`, `OpenBisListResult`, `OpenBisDetailsResult`), and Python helpers (`check_connection`, `fetch_sample_types`, `fetch_samples_by_type`, `fetch_sample_details`, `create_openbis_connection`, `classify_error`).
- Produces:
  - Python `openbis_fetch.run_worker_loop(stdin_stream, stdout_stream) -> int` supporting single-line JSON requests `{"action": "check"|"types"|"list"|"details", "url": ..., "token": ..., "sampleType": ..., "permId": ..., "skipEln": bool}` with credential-keyed `pybis.Openbis` reuse and `--worker` CLI dispatch in `main`.
  - C# `OpenBisClient : IDisposable` with lazy `--worker` process management (`WarmUpWorkerInBackground()`, `Dispose()`, `ShutdownWorker()`), automatic fallback to one-shot CLI when stub scripts reject `--worker`, and clean teardown/reset when the worker process crashes or writes malformed stdout (`Review Focus #1`).

- [ ] **Step 1: Write failing Python tests for `--worker` mode in `tests/test_openbis_fetch.py`**

Add tests:
- `test_run_worker_loop_reuses_session_for_matching_credentials_and_dispatches_all_actions(tmp_path)`: supplies 5 newline-terminated JSON requests (`check`, `types`, `list`, `details` with `skipEln=False`, `details` with `skipEln=True` for same credentials, plus one `list` with a new token) via `io.StringIO` with patched `create_openbis_connection` and `requests.post`; asserts `create_openbis_connection` is called only TWICE (once per unique `(url, token)` pair, not 4 times), `fetch_sample_details` honors `skipEln`, every line on stdout parses as JSON matching `success=True`, and exit code is `0`.
- `test_run_worker_loop_handles_invalid_json_without_crashing()`: feeds invalid JSON line followed by valid `check` request; asserts first response has `{"success": False, "errorType": "unknown_error"}` and loop still processes the second request cleanly.
- `test_main_dispatches_worker_flag(monkeypatch)`: mocks `run_worker_loop` returning `0`, calls `main(["openbis_fetch.py", "--worker"])`, asserts `run_worker_loop` invoked and returns `0` without calling `parse_cli_args`.

- [ ] **Step 2: Write failing C# tests in `OpenBISLabelStudio.Tests/Program.cs`**

Add tests to `OpenBISLabelStudio.Tests/Program.cs`:
- `OpenBisClientWorkerReusesPersistentPythonProcessAcrossQueries`: creates a temporary Python script supporting `--worker` that increments a file counter once on startup and answers two JSON requests (`--check` and `--types`) over `stdin`/`stdout`; executes `CheckConnectionAsync` and `QueryTypesAsync` sequentially and asserts startup counter file equals `"1"`, both DTOs have `Success == true`, and after `client.Dispose()`, the worker process exits cleanly so the temp dir deletes without file lock.
- `OpenBisClientWorkerHandlesMidSessionCrashAndCorruptStdoutCleanly` *(Review Focus #1)*: creates a temp `--worker` Python script that succeeds on request 1, emits `"<<<corrupt-json>>>"` and exits on request 2, and succeeds cleanly on respawn for request 3; asserts request 1 succeeds, request 2 returns `Success == false` with `ErrorType == "unknown_error"` (not hanging or throwing), and request 3 succeeds on a fresh worker!
- `OpenBisClientFallsBackToCliWhenStubScriptRejectsWorker`: creates a stub Python script that rejects `--worker` (prints `"Invalid arguments"` to stderr and exits 1) and handles `--types ... --out <path>` via CLI; asserts `client.QueryTypesAsync(...)` succeeds cleanly using the CLI fallback.

- [ ] **Step 3: Run Python and C# tests to verify failure**

Run:
```powershell
pytest tests/test_openbis_fetch.py -v
```
Expected: New worker tests FAIL (`AttributeError: module 'openbis_fetch' has no attribute 'run_worker_loop'`).

Run MSBuild Debug build + C# tests -> Expected: C# compilation succeeds after stubs or tests added fail against current `OpenBisClient` (`IDisposable` / worker behavior missing).

- [ ] **Step 4: Implement `--worker` in `OpenBISLabelStudio.Backend/openbis_fetch.py`**

1. Keep `parse_cli_args` 5-tuple return signature unchanged.
2. Update `fetch_sample_types(openbis_url, token, session=None)`, `fetch_samples_by_type(openbis_url, token, sample_type, session=None)`, and `fetch_sample_details(openbis_url, token, perm_id, skip_eln=False, session=None)` to accept optional pre-authenticated `session` (`o = session if session is not None else create_openbis_connection(openbis_url, token)`).
3. Add `WorkerSessionCache` + `run_worker_loop(stdin_stream=None, stdout_stream=None) -> int`:
   - Reads UTF-8 lines until EOF (`line == ""`), ignoring blank lines.
   - Caches the active `pybis.Openbis` client keyed by `(url, token)` for `types` / `list` / `details` calls; if a cached session call raises an exception, clears the cached session and retries once if appropriate or classifies error cleanly (never retaining a broken session). Uses fast `check_connection(url, token)` directly for `"check"`.
   - Writes single-line UTF-8 JSON (`json.dumps(result, ensure_ascii=False)`) followed by `\n` and `flush()`. Never writes stray print statements to `stdout` during worker mode.
4. In `main(argv=None)`: check `args_without_exe` (first arg `--worker`) BEFORE `parse_cli_args` and invoke `run_worker_loop(sys.stdin, sys.stdout)`.

- [ ] **Step 5: Implement worker lifecycle + CLI fallback in `OpenBISLabelStudio.Backend/Services/OpenBisClient.cs`**

1. Make `OpenBisClient : IDisposable` with `SemaphoreSlim _workerGate = new SemaphoreSlim(1, 1)`, `_workerProcess`, `_workerIn`, `_workerOut`, `_workerUnsupported` flag, `ShutdownWorker()`, and static `AppDomain.CurrentDomain.ProcessExit` cleanup tracking active worker processes so orphaned Python children exit when the host exits cleanly.
2. Add `WarmUpWorkerInBackground()` (starts `--worker` eagerly on background thread with UTF-8 stdio and swallows errors cleanly if script/Python missing).
3. Track whether `--worker` succeeded or failed during handshake before any first valid JSON line:
   - Before writing a request to `--worker`, if `!_workerUnsupported`, try starting/sending `{"action":..., "url":..., "token":..., "sampleType":..., "permId":..., "skipEln":...}` on `stdin` and reading one line from `stdout` (respecting optional `CancellationToken` / gate wait).
   - If the worker produces NO valid JSON response BEFORE ANY worker query has ever succeeded (e.g., stub script rejected `--worker` with exit 1), set `_workerUnsupported = true`, clean up process, and immediately fall back to existing `RunScriptAsync(args, errorFactory)`.
   - Once at least one worker request has succeeded (`_workerConfirmed = true`), if a subsequent call encounters EOF, process crash, or corrupt non-JSON stdout line, dispose the crashed worker process immediately (`ResetWorkerProcessUnsafe()`) so the next call spawns a fresh worker, and return `DeserializeOrError` (`"unknown_error"`) for the failed call.
4. Keep existing public method signatures (`CheckConnectionAsync`, `QueryTypesAsync`, `QuerySamplesByTypeAsync`, `QuerySampleDetailsAsync`, `DeserializeOrError`, `ScriptPath`) intact.

- [ ] **Step 6: Run Python and C# tests to verify everything passes**

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" OpenBISLabelStudio.slnx /t:Restore,Build /p:Configuration=Debug /v:minimal
.\OpenBISLabelStudio.Tests\bin\Debug\net10.0-windows\OpenBISLabelStudio.Tests.exe
pytest tests/test_openbis_fetch.py -v
```
Expected: C# tests execute all 29 tests with 0 failures; `pytest` passes all 20 tests.

- [ ] **Step 7: Commit Task 2**

```bash
git add OpenBISLabelStudio.Backend/openbis_fetch.py OpenBISLabelStudio.Backend/Services/OpenBisClient.cs tests/test_openbis_fetch.py OpenBISLabelStudio.Tests/Program.cs
git commit -m "feat: add persistent python --worker session cache with safe CLI fallback"
```

---

### Task 3: Define `ILabelStudioBackend` Contracts, PNG Rendering Helpers, and `LabelStudioBackend` Service

**Files:**
- Create: `OpenBISLabelStudio.Backend/Contracts/BackendContracts.cs`
- Create: `OpenBISLabelStudio.Backend/Services/LabelStudioBackend.cs`
- Modify: `OpenBISLabelStudio.Backend/Services/LabelPreviewRenderer.cs:30-460,1170-1200`
- Test: `OpenBISLabelStudio.Tests/Program.cs`

**Interfaces:**
- Consumes: `AppConfigService`, `OpenBisClient` (`IDisposable` + worker + CLI), `LabelPreviewRenderer`, `TokenRecoveryCoordinator`, `OpenBISLabelStudio.Models.*`.
- Produces:
  - Namespace `OpenBISLabelStudio.Backend.Contracts`:
    - `ITokenRecoveryPrompter`: `Task<bool> PromptForNewTokenAsync(string currentUrl, string warningMessage, CancellationToken ct = default);`
    - DTOs: `PrinterDropdownState`, `TypeOptionDto`, `ObjectTypeRowDto`, `WorkspaceStartupState`, `BackgroundTypeRefreshResult`, `ConnectionCheckResult`, `SettingsSnapshotDto`, `ServerTypeSyncResult`, `WizardStepSaveRequest`, `SettingsSaveRequest`, `OperationResult`, `SampleSummaryDto`, `SampleListResultDto`, `SamplePreviewBundleDto`, `PrintSlotSelection`.
    - `ILabelStudioBackend : IDisposable`:
      - `void WarmUpInBackground();`
      - `Task<WorkspaceStartupState> InitializeWorkspaceAsync(ITokenRecoveryPrompter? recoveryPrompter = null, CancellationToken ct = default);`
      - `Task<BackgroundTypeRefreshResult> RefreshWorkspaceTypesAsync(ITokenRecoveryPrompter? recoveryPrompter = null, CancellationToken ct = default);`
      - `Task<ConnectionCheckResult> TestConnectionAsync(string url, string token, bool persistOnSuccess, CancellationToken ct = default);`
      - `Task<SettingsSnapshotDto> GetSettingsSnapshotAsync(CancellationToken ct = default);`
      - `Task<ServerTypeSyncResult> FetchAndMergeServerTypesAsync(string url, string token, IReadOnlyList<ObjectTypeRowDto> currentRows, CancellationToken ct = default);`
      - `Task<Dictionary<LabelTemplateType, byte[]>> GetExamplePreviewsAsync(LabelTemplateProfile profile, Action<LabelTemplateType, byte[]>? onSlotReady = null, CancellationToken ct = default);`
      - `Task<OperationResult> SaveWizardStepAsync(WizardStepSaveRequest request, CancellationToken ct = default);`
      - `Task<OperationResult> SaveSettingsOfflineAsync(SettingsSaveRequest request, CancellationToken ct = default);`
      - `Task<SampleListResultDto> LoadObjectsForTypeAsync(string typeCode, ITokenRecoveryPrompter? recoveryPrompter = null, CancellationToken ct = default);`
      - `void ClearActiveSample();`
      - `Task<SamplePreviewBundleDto> LoadSamplePreviewsAsync(string typeCode, string permId, string magnetFreeText, string flaskFreeText, Action<LabelTemplateType, byte[]>? onSlotReady = null, ITokenRecoveryPrompter? recoveryPrompter = null, CancellationToken ct = default);`
      - `Task<byte[]> RenderSlotWithFreeTextAsync(LabelTemplateType slot, string freeText, CancellationToken ct = default);`
      - `Task<OperationResult> UpdatePrinterSelectionsAsync(string smallBoxPrinter, string magnetPrinter, string flaskPrinter);`
      - `Task<LabelPrintBatchResult> PrintSelectedLabelsAsync(IReadOnlyList<PrintSlotSelection> selections, CancellationToken ct = default);`
      - `byte[] GetLoadingPreviewPng(LabelTemplateType slot, LabelTemplateProfile profile);`
      - `byte[] GetFallbackPreviewPng(LabelTemplateType slot, LabelTemplateProfile profile, string reason);`
  - Class `OpenBISLabelStudio.Backend.Services.LabelStudioBackend : ILabelStudioBackend`:
    - Constructors: `LabelStudioBackend(string? baseDirectory = null, string? pythonExecutable = null, Func<IReadOnlyList<string>>? installedPrintersProvider = null)` and `LabelStudioBackend(AppConfigService configService, OpenBisClient openBisClient, LabelPreviewRenderer previewRenderer, Func<IReadOnlyList<string>>? installedPrintersProvider = null)`.
    - Static helpers: `RunPreviewWorkerLoop(TextReader input, TextWriter output)`, `ShutdownPreviewWorker()`, `BuildInitialTypeRows(IEnumerable<OpenBisTypeInfoDto> serverTypes, IReadOnlyList<ConfiguredOpenBisType> existingConfigured)`, `ResolveTemplateProfile(string? selectedTypeCode, IReadOnlyList<ConfiguredOpenBisType>? configuredTypes, string? sampleDetailsType)`, `CredentialsChangedSinceRequest(string? requestUrl, string? requestToken, AppConfig? currentConfig)`.
  - `LabelPreviewRenderer` PNG helpers: `RenderTemplatePreviewPngAsync(...)`, `RenderExampleSlotPreviewPngAsync(...)`, `CreateLoadingPreviewPng(...)`, `CreateFallbackPreviewPng(...)`, `EncodeImageAsPngBytes(Image image)`.

- [ ] **Step 1: Write failing backend facade unit tests in `OpenBISLabelStudio.Tests/Program.cs`**

Add tests exercising `LabelStudioBackend` directly without creating any WinForms `Form`:
1. `BackendExpiresTokenRecoveryCoalescesAndRetriesWithoutUiForms` *(Review Focus #2)*: injects a mock Python script that fails with `{"success":false,"errorType":"token_expired","error":"Token invalid"}` when called with `"old-tok"` and succeeds with `[ {"permId":"P-1","name":"Obj-1"} ]` once `"new-tok"` is saved; uses a test `ITokenRecoveryPrompter` that counts prompts (`promptCount`), saves `"new-tok"` on retry 1 -> returns `Success == true`, `Samples.Count == 1`, `promptCount == 1` even with two concurrent `LoadObjectsForTypeAsync("CRYSTAL", prompter)` calls; also tests canceled prompter (`returns false`) returning `WasCancelled == true` and non-empty `NoticeMessage` cleanly.
2. `BackendLoadSamplePreviewsAndFreeTextIsolationPreventsStaleState` *(Review Focus #3)*:
   - Calling `RenderSlotWithFreeTextAsync(LabelTemplateType.Magnet12mm, "custom")` before any sample is loaded returns empty `byte[]` without throwing.
   - Calling `PrintSelectedLabelsAsync(...)` before any sample is loaded returns `AllSucceeded == false` with `"No active sample loaded. Click 'Load Templates' first."`.
   - Loading a sample via `LoadSamplePreviewsAsync("SUBSTRATE", "PERM-1", "M1", "F1", onSlotReady, ...)` skips ELN (`--skip-eln` flag observed by stub Python script for Generic profile), invokes `onSlotReady` 3 times with valid PNG bytes (`89 50 4E 47` PNG header, falling back cleanly when templates missing), sets `IsGenericProfile == true`; subsequent `RenderSlotWithFreeTextAsync(LabelTemplateType.Magnet12mm, "M2")` returns valid PNG bytes; calling `ClearActiveSample()` or switching `LoadObjectsForTypeAsync` clears the active sample so later `RenderSlotWithFreeTextAsync` returns empty `byte[]`.
3. `BackendValidatesAndSavesWizardAndSettingsIsolatingBaseDirectory` *(Review Focus #5 partial)*: tests `SaveSettingsOfflineAsync` and `SaveWizardStepAsync` URL/token/empty-types validation rules (`"Enter a valid openBIS Instance URL starting with http:// or https://."`, `"Enter an openBIS Personal Access Token."`, `"Select at least one object type."`), verifies `config/*.json` persistence under `tempRoot` only, verifies `TestConnectionAsync` focuses token on auth error and persists only when `persistOnSuccess: true`.
4. `BackendHelperMethodsBuildTypesResolveProfileAndDetectCredentialChanges`: unit tests for `LabelStudioBackend.BuildInitialTypeRows`, `ResolveTemplateProfile`, and `CredentialsChangedSinceRequest` (ready for Form1/SetupSettingsForm removal in Task 4).

- [ ] **Step 2: Run build + tests to confirm failure**

Run:
```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" OpenBISLabelStudio.slnx /t:Restore,Build /p:Configuration=Debug /v:minimal
```
Expected: Compile FAIL (`ILabelStudioBackend`, `LabelStudioBackend`, `BackendContracts` not defined yet).

- [ ] **Step 3: Add PNG byte helpers to `OpenBISLabelStudio.Backend/Services/LabelPreviewRenderer.cs`**

1. Add `public static byte[] EncodeImageAsPngBytes(Image image)` (cloning or saving safely into `MemoryStream` as `ImageFormat.Png` -> `ToArray()`).
2. Add `public async Task<byte[]> RenderTemplatePreviewPngAsync(LabelTemplateType slot, LabelTemplateProfile profile, SampleDetailsDto details, string freeText = "", CancellationToken ct = default)` and `public async Task<byte[]> RenderExampleSlotPreviewPngAsync(LabelTemplateType slot, LabelTemplateProfile profile, CancellationToken ct = default)`:
   - Reads cached disk PNG bytes (`File.ReadAllBytesAsync`) or decodes base64 PNG straight from `--preview-worker` when available without extra bitmap re-encoding, and disposes temporary `Image`/`Bitmap` objects when encoding fallback/in-proc STA renders.
3. Add `public static byte[] CreateLoadingPreviewPng(LabelTemplateType slot, LabelTemplateProfile profile)` and `public static byte[] CreateFallbackPreviewPng(LabelTemplateType slot, LabelTemplateProfile profile, string reason)` (creating bitmap inside `using`, encoding to PNG `byte[]`).

- [ ] **Step 4: Create `OpenBISLabelStudio.Backend/Contracts/BackendContracts.cs`**

Define `ITokenRecoveryPrompter`, `ILabelStudioBackend : IDisposable`, and all sealed contract DTOs (`PrinterDropdownState`, `TypeOptionDto`, `ObjectTypeRowDto`, `WorkspaceStartupState`, `BackgroundTypeRefreshResult`, `ConnectionCheckResult`, `SettingsSnapshotDto`, `ServerTypeSyncResult`, `WizardStepSaveRequest`, `SettingsSaveRequest`, `OperationResult`, `SampleSummaryDto`, `SampleListResultDto`, `SamplePreviewBundleDto`, `PrintSlotSelection`) matching the exact signatures above.

- [ ] **Step 5: Implement `OpenBISLabelStudio.Backend/Services/LabelStudioBackend.cs`**

Implement `ILabelStudioBackend` cleanly by migrating all business logic from `Form1.cs` and `SetupSettingsForm.cs`:
- **Active sample state guard (`_activeSampleLock`):** holds `_activeSample` (`Details`, `Profile`, `TypeCode`, `PermId`). `ClearActiveSample()` clears it; `LoadObjectsForTypeAsync(...)` clears `_activeSample` at entry; `LoadSamplePreviewsAsync(...)` sets `_activeSample` only when details succeed and `!ct.IsCancellationRequested`.
- **Synchronous completion for disk/validation calls (`GetSettingsSnapshotAsync`, `InitializeWorkspaceAsync`, `SaveSettingsOfflineAsync`, `SaveWizardStepAsync`, `UpdatePrinterSelectionsAsync`):** complete inline via `Task.FromResult(...)` so WinForms button handlers behave deterministically in synchronous UI tests.
- **`InitializeWorkspaceAsync` & `GetSettingsSnapshotAsync`:** inspect `_configService.GetFirstMissingWizardStep()`, load saved `ConfiguredOpenBisType`s formatted (`DisplayName` or `FormatFriendlyName(Code)`) and sorted `StringComparer.CurrentCultureIgnoreCase` (`WorkspaceStartupState` / `SettingsSnapshotDto`), and resolve printers via `AppConfigService.ResolveBrotherPrinterDropdownItems(_installedPrintersProvider?.Invoke() ?? GetSystemInstalledPrinters(), printerConfig)`.
- **`TestConnectionAsync`:** validate `url` (`"Enter a valid openBIS Instance URL starting with http:// or https://."`) and `token` (`"Enter an openBIS Personal Access Token."`), call `_openBisClient.CheckConnectionAsync(url, token)`, set `ShouldFocusToken = string.IsNullOrEmpty(token) || string.Equals(result.ErrorType, "token_expired", StringComparison.OrdinalIgnoreCase)`, and when `Success && persistOnSuccess`, save via `_configService.SaveAppConfig(...)` catching persistence errors cleanly (`"Could not save configuration files: {ex.Message}"`).
- **`SaveWizardStepAsync` & `SaveSettingsOfflineAsync`:** validate URL/token (for `Connection` step or offline save), require `>= 1` selected type (`"Select at least one object type."` for `ObjectTypes` step or offline save), save the corresponding `config/*.json` files via `_configService`, and catch I/O errors cleanly (`"Could not save configuration files: {ex.Message}"`).
- **`UpdatePrinterSelectionsAsync`:** save `PrinterConfig` via `_configService.SavePrinterConfig(...)` and return `OperationResult` cleanly.
- **`ExecuteWithExpiredTokenRecoveryAsync<TResult>`** (moved from `Form1.cs`): captures `(url, token)` snapshot at request start, retries once automatically if persisted credentials changed while in flight (`CredentialsChangedSinceRequest`), otherwise coalesces prompts via `TokenRecoveryCoordinator` calling `recoveryPrompter.PromptForNewTokenAsync(...)` with serialized warning detail, reloads `AppConfig` on `true`, and retries cleanly.
- **`LoadObjectsForTypeAsync`:** calls `ClearActiveSample()`, verifies saved `OpenBisUrl` is present (`"openBIS URL is missing. Please configure it in Settings..."`), queries `_openBisClient.QuerySamplesByTypeAsync` through `ExecuteWithExpiredTokenRecoveryAsync`, maps `SampleDto` to `SampleSummaryDto`, and returns `WasCancelled` / friendly notice on canceled recovery or `ErrorMessage` cleanly.
- **`FetchAndMergeServerTypesAsync`:** validates URL/token (`"Enter a valid openBIS Instance URL starting with http:// or https://."` / `"Enter an openBIS Personal Access Token."`), calls `_openBisClient.QueryTypesAsync`, merges via `BuildInitialTypeRows` while overlaying user's in-progress `currentRows` (`IsSelected`, custom `TemplateProfile`) by case-insensitive `Code`, and sets `IsTokenExpired` cleanly when `ErrorType == "token_expired"`.
- **`RefreshWorkspaceTypesAsync`**: fetches server types with expired-token recovery; when server returns types, preserves each configured row's `Code` and `TemplateProfile`, updates `DisplayName` if changed, saves `openbis_types.json`, and returns sorted `ConfiguredTypes`.
- **`LoadSamplePreviewsAsync`**: computes `LabelTemplateProfile` via `ResolveTemplateProfile` and `skipEln = (profile == LabelTemplateProfile.Generic)`; fetches `--details`; renders `SmallBox12mm`, `Magnet12mm`, `Flask24mm` PNG bytes concurrently via `_previewRenderer.RenderTemplatePreviewPngAsync` invoking `onSlotReady?.Invoke(slot, pngBytes)` as each slot finishes (wrapping per-slot renderer exceptions in `CreateFallbackPreviewPng` so one failed slot never breaks the others).
- **`GetExamplePreviewsAsync`**: renders all 3 slots for `profile` via `_previewRenderer.RenderExampleSlotPreviewPngAsync`, invoking `onSlotReady` as each finishes, returning fallback PNG on error (never leaking raw `bpac` exceptions).
- **`PrintSelectedLabelsAsync`**: validates active sample loaded (`"No active sample loaded. Click 'Load Templates' first."`) and non-empty selections (`"No label templates selected for printing."`), ignores `FreeText` when active profile is `Molecule`, constructs `LabelPrintJobRequest` list using labels `"Small Box 12mm Label"`, `"Magnet 12mm Label"`, `"Flask 24mm Label"`, and awaits `_previewRenderer.PrintLabelsAsync(...)`.
- **`Dispose()`**: disposes `_openBisClient`. (Keep `WarmUpInBackground()` calling `_previewRenderer.WarmUpWorkerInBackground()` and best-effort `_openBisClient.WarmUpWorkerInBackground()` when valid `openbis_config.json` exists and `_installedPrintersProvider == null`.)
- Keep `No-UI-types rule`: zero `System.Windows.Forms.Form`, `Control`, `MessageBox`, `PictureBox`, `ProgressBar`, or `Application` references anywhere in `OpenBISLabelStudio.Backend`.

- [ ] **Step 6: Run Debug & Release MSBuild builds and C# tests**

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" OpenBISLabelStudio.slnx /t:Restore,Build /p:Configuration=Debug /v:minimal
.\OpenBISLabelStudio.Tests\bin\Debug\net10.0-windows\OpenBISLabelStudio.Tests.exe
```
Expected: Build exits 0; all C# tests (including all new Task 3 tests + `BackendAssemblyHasNoUiControlDependencies`) pass (`0 failed`).

- [ ] **Step 7: Commit Task 3**

```bash
git add OpenBISLabelStudio.Backend/ OpenBISLabelStudio.Tests/Program.cs
git commit -m "feat: add ILabelStudioBackend contract, PNG preview helpers, and LabelStudioBackend"
```

---

### Task 4: Refactor `SetupSettingsForm`, `Form1`, and `Program.cs` to Thin UI Consuming `ILabelStudioBackend`

**Files:**
- Create: `OpenBISLabelStudio/FormTokenRecoveryPrompter.cs`
- Modify: `OpenBISLabelStudio/Program.cs:1-36`
- Modify: `OpenBISLabelStudio/SetupSettingsForm.cs`
- Modify: `OpenBISLabelStudio/Form1.cs`
- Test: `OpenBISLabelStudio.Tests/Program.cs`

**Interfaces:**
- Consumes: `ILabelStudioBackend`, `ITokenRecoveryPrompter`, `OpenBISLabelStudio.Backend.Contracts.*`, `OpenBISLabelStudio.Backend.Services.LabelStudioBackend` (in `Program.cs` only), `OpenBISLabelStudio.Models` (`LabelTemplateProfile`, `LabelTemplateType`, `SetupWizardStep`, `LabelPrintBatchResult`, `LabelPrintJobResult`).
- Produces:
  - `OpenBISLabelStudio/FormTokenRecoveryPrompter.cs` implementing `ITokenRecoveryPrompter` (`PromptForNewTokenAsync` marshaling onto UI owner via `InvokeRequired` / `Invoke` / `BeginInvoke` and opening `SetupSettingsForm(backend, SetupDialogMode.ExpiredTokenRecovery, SetupWizardStep.Connection, warningMessage)`).
  - Thin `SetupSettingsForm(ILabelStudioBackend backend, SetupDialogMode mode, SetupWizardStep startStep = SetupWizardStep.Connection, string? initialWarning = null)` exposing `Mode`, `TypesSyncedFromServer` (`bool`), `ApplyTypeRows(IReadOnlyList<ObjectTypeRowDto> rows, bool syncedFromServer = true)`, and `TrySaveSettingsOffline(out string validationError)`.
  - Thin `Form1(ILabelStudioBackend backend)` + parameterless `Form1()` (constructing default `LabelStudioBackend` solely when opened in WinForms designer/smoke test) holding zero `AppConfig`/`OpenBisClient`/`LabelPreviewRenderer`/`ConfiguredOpenBisType`/`PrinterConfig`/`SampleDto`/`SampleDetailsDto` fields.
  - Safe UI helper `DecodePngBitmap(byte[]? pngBytes, Func<byte[]>? fallbackFactory = null)` decoding `MemoryStream` into an independent `new Bitmap(img)` cleanly without throwing on empty/corrupt byte payloads (`Review Focus #4`).

- [ ] **Step 1: Add/update failing UI boundary & mock-backend WinForms tests in `OpenBISLabelStudio.Tests/Program.cs`**

1. Update existing UI tests in `OpenBISLabelStudio.Tests/Program.cs`:
   - Convert `Form1CredentialsChanged...`, `Form1ResolveTemplateProfile...`, and `BuildInitialTypeRows...` calls to target `LabelStudioBackend.*` (preserving every existing assertion).
   - Update `SetupSettingsFormInitializesSafelyWithZeroPrinters`, `SetupSettingsFormExpiredTokenModePreFillsUrlAndClearsToken`, `SetupSettingsFormSettingsSavePersistsOfflineAndCancelDiscards`, `SetupSettingsFormTrySaveSettingsOfflineReturnsErrorWhenDiskSaveFails`, and `Form1LoadsCachedTypesOfflineHandlesZeroPrintersAndAutoSavesPrinterChanges` to pass `new LabelStudioBackend(configService, client, renderer, installedPrintersProvider)` (and update line 1036 `form.ApplyServerTypes(...)` -> `form.ApplyTypeRows(new[] { new ObjectTypeRowDto { IsSelected = true, Code = "SUBSTANCE", DisplayName = "Substance", TemplateProfile = LabelTemplateProfile.Molecule } })`).
   - Update `Form1HasSettingsButtonWithoutOverlaps` if needed (`new Form1()`).
2. Add two new Task 4 tests:
   - `Form1AndSetupSettingsFormRenderWithMockBackendAndSurviveCorruptPngBytes` *(Review Focus #4)*: creates a pure in-memory `StubLabelStudioBackend : ILabelStudioBackend` (no disk directories, no Python, no `bpac`, returning valid 1x1 PNG bytes for `SmallBox12mm`, corrupt garbage `new byte[] { 1, 2, 3 }` for `Magnet12mm`, and `Array.Empty<byte>()` for `Flask24mm` while recording calls); opens `Form1(stubBackend)` offscreen, selects a type, clicks Print/Load or invokes preview paths, verifies `openbisTypeComboBox` populates, `DecodePngBitmap` handles corrupt/empty `byte[]` cleanly without throwing, and opening `SetupSettingsForm(stubBackend, SetupDialogMode.Settings)` populates Connection/Types/Printers cleanly.
   - `FrontendFormsContainNoBackendServiceOrSecretReferences`: reflects over `Form1` and `SetupSettingsForm` fields/constructors/methods confirming neither type references `AppConfigService`, `OpenBisClient`, `LabelPreviewRenderer`, `AppConfig`, `ConfiguredOpenBisType`, `PrinterConfig`, `SampleDetailsDto`, or `Newtonsoft.Json` / `bpac` types.

- [ ] **Step 2: Run Debug MSBuild to observe compile failure before refactoring UI**

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" OpenBISLabelStudio.slnx /t:Restore,Build /p:Configuration=Debug /v:minimal
```
Expected: Compile FAIL (`Form1` / `SetupSettingsForm` still use old constructors and lack `ApplyTypeRows`).

- [ ] **Step 3: Create `OpenBISLabelStudio/FormTokenRecoveryPrompter.cs` and update `OpenBISLabelStudio/Program.cs`**

1. `FormTokenRecoveryPrompter(Form owner, ILabelStudioBackend backend)`: `PromptForNewTokenAsync(string currentUrl, string warningMessage, CancellationToken ct)` marshals cleanly onto `owner` UI thread (or returns `false` if owner disposed/canceled), displays `new SetupSettingsForm(_backend, SetupDialogMode.ExpiredTokenRecovery, SetupWizardStep.Connection, warningMessage)`, and returns `DialogResult == DialogResult.OK`.
2. `Program.cs`: import `OpenBISLabelStudio.Backend.Services;` (removing `OpenBISLabelStudio.Services;`), route `--preview-worker` to `LabelStudioBackend.RunPreviewWorkerLoop(Console.In, Console.Out)`, instantiate `using var backend = new LabelStudioBackend(); Application.Run(new Form1(backend));`, and clean up via `LabelStudioBackend.ShutdownPreviewWorker()` in `finally`.

- [ ] **Step 4: Refactor `OpenBISLabelStudio/SetupSettingsForm.cs` onto `ILabelStudioBackend`**

1. Replace `_configService`, `_openBisClient`, `_previewRenderer`, `_installedPrintersProvider`, `_previewCache`, `ApplyServerTypes`, `BuildInitialTypeRows` with `_backend` (`ILabelStudioBackend`) and PNG byte cache `_previewPngCache` (`Dictionary<(LabelTemplateProfile, LabelTemplateType), byte[]>`).
2. `InitializeFromConfig(startStep)` calls `_backend.GetSettingsSnapshotAsync().GetAwaiter().GetResult()` to populate URL/token (cleared in `ExpiredTokenRecovery` mode), master `ObjectTypeRowDto` grid rows, and printer dropdowns + `lblNoPrintersWarning`.
3. `RunLiveConnectionCheckAsync(saveCredentialsOnSuccess)` calls `_backend.TestConnectionAsync(url, token, persistOnSuccess: saveCredentialsOnSuccess)` and focuses `txtToken` when `ShouldFocusToken == true`.
4. `LoadServerTypesForCurrentPageAsync(redirectToConnectionOnExpiredToken)` calls `_backend.FetchAndMergeServerTypesAsync(url, token, GetMasterTypeRowsSnapshot())`, updates `_masterTypeRows` preserving checked filter/selection state, sets `TypesSyncedFromServer = true` on success, or handles `IsTokenExpired` / offline banner cleanly.
5. `ShowExamplePreviews` / `LoadAndPrewarmExamplePreviewsAsync` uses `_backend.GetLoadingPreviewPng` and `_backend.GetExamplePreviewsAsync(profile, onSlotReady, ct)` with a shared `DecodePngBitmap(byte[] pngBytes, byte[] fallbackPng)` UI helper on the UI thread (`BeginInvoke` / `InvokeRequired` safe if `onSlotReady` fires off-thread!). Note: ALWAYS marshal `onSlotReady` back via `BeginInvoke` onto the WinForms UI thread before touching `PictureBox`!
6. `TrySaveSettingsOffline(out string validationError)` delegates to `_backend.SaveSettingsOfflineAsync(new SettingsSaveRequest { ... }).GetAwaiter().GetResult()`.
7. Wizard Next/Finish delegates step persistence cleanly to `_backend.SaveWizardStepAsync(...)` and `_backend.TestConnectionAsync(..., persistOnSuccess: true)` cleanly, keeping exact status/error text and `UseMnemonic = false` & progress bar visibility behavior intact.

- [ ] **Step 5: Refactor `OpenBISLabelStudio/Form1.cs` onto `ILabelStudioBackend`**

1. Constructor `Form1() : this(new LabelStudioBackend()) { }` and `Form1(ILabelStudioBackend backend)`. Keep zero `AppConfigService` / `OpenBisClient` / `LabelPreviewRenderer` / raw config / raw `SampleDto` / raw `SampleDetailsDto` fields!
2. Create `private FormTokenRecoveryPrompter RecoveryPrompter => new FormTokenRecoveryPrompter(this, _backend);` (or cache instance `_recoveryPrompter`).
3. `Form1_Load` calls `WorkspaceStartupState startup = await _backend.InitializeWorkspaceAsync(RecoveryPrompter)`. If `startup.MissingWizardStep != SetupWizardStep.None`, opens `SetupSettingsForm(_backend, SetupDialogMode.Wizard, startup.MissingWizardStep)`, exits via `Close()` if still incomplete on Cancel, otherwise calls `InitializeWorkspaceAsync` again to populate `openbisTypeComboBox` (`TypeOptionDto`), printer combos (`PrinterDropdownState`), Free Text state, and `_backend.WarmUpInBackground()`. If `!wizard.TypesSyncedFromServer`, kicks off `_backend.RefreshWorkspaceTypesAsync(RecoveryPrompter)` and updates `openbisTypeComboBox` / status notice banner on completion.
4. `openbisTypeComboBox_SelectedIndexChanged` reads `TypeOptionDto` from `openbisTypeComboBox.SelectedItem`, increments `_typeSelectionRequestVersion` and `_sampleDetailsRequestVersion`, clears UI sample dropdown/previews, calls `_backend.ClearActiveSample()`, sets `_hasLoadedSample = false`, updates `magnetFreeTextBox` / `flaskFreeTextBox` enabled/placeholder state based on `selectedItem.TemplateProfile`, and for real types populates `openbisObjectsComboBox` directly with `SampleSummaryDto` items returned by `await _backend.LoadObjectsForTypeAsync(selectedItem.Code, RecoveryPrompter)` (eliminating any separate `_loadedSamples` list) while driving `prgLoading` marquee progress cleanly.
5. `loadPrintTemplates_Click` reads `SampleSummaryDto` from `openbisObjectsComboBox.SelectedItem`, passes `(typeCode, selectedSample.PermId, magnetFreeTextBox.Text, flaskFreeTextBox.Text, onSlotReady, RecoveryPrompter, ct)` to `_backend.LoadSamplePreviewsAsync(...)` — displaying loading bitmaps immediately (`_backend.GetLoadingPreviewPng`), updating each slot progressively via `BeginInvoke` in `onSlotReady` + safe `DecodePngBitmap(...)`, tracking `_hasLoadedSample = bundle.Success`, and checking all 3 checkboxes on success.
6. `magnetFreeTextBox_Leave` / `flaskFreeTextBox_Leave` (and Enter handlers) check `_hasLoadedSample && _activeProfile == LabelTemplateProfile.Generic` and call `_backend.RenderSlotWithFreeTextAsync(slot, text, cts.Token)`.
7. `PrinterComboBox_SelectedIndexChanged` calls `_backend.UpdatePrinterSelectionsAsync(...)`.
8. `btnPrintLabel_Click_1` collects checked `PrintSlotSelection`s and awaits `_backend.PrintSelectedLabelsAsync(selections)`, displaying formatted `LabelPrintBatchResult` dialog.
9. `btnSettings_Click` increments `_typeRefreshRequestVersion`, opens `SetupSettingsForm(_backend, SetupDialogMode.Settings, SetupWizardStep.Connection)`, reloads workspace snapshot (`InitializeWorkspaceAsync`), re-renders current sample if profile changed, or resets UI + calls `_backend.ClearActiveSample()` if type was removed/credentials changed.

- [ ] **Step 6: Run MSBuild builds (Debug & Release) and C# test suite**

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" OpenBISLabelStudio.slnx /t:Restore,Build /p:Configuration=Debug /v:minimal
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" OpenBISLabelStudio.slnx /t:Restore,Build /p:Configuration=Release /v:minimal
.\OpenBISLabelStudio.Tests\bin\Debug\net10.0-windows\OpenBISLabelStudio.Tests.exe
```
Expected: Both Debug & Release builds succeed (`0 warnings, 0 errors`); C# tests print all tests passing (`0 failed`).

- [ ] **Step 7: Commit Task 4**

```bash
git add OpenBISLabelStudio/ OpenBISLabelStudio.Tests/Program.cs
git commit -m "refactor: make Form1 and SetupSettingsForm thin WinForms views over ILabelStudioBackend"
```

---

### Task 5: Update `README.md`, Run Secret Hygiene Audit & Verify MSBuild Release Publish

**Files:**
- Modify: `README.md`

**Interfaces:**
- Consumes: Completed `OpenBISLabelStudio.Backend`, `OpenBISLabelStudio`, `OpenBISLabelStudio.Tests`, and `tests/test_openbis_fetch.py`.
- Produces: Updated `README.md` matching the two-project Backend/Frontend architecture and publish layout, plus verified secret-free Release publish output and clean green test runs across Debug, Release, and Python.

- [ ] **Step 1: Update `README.md` cleanly for Backend/Frontend separation**

Update project structure tree, architecture notes (`OpenBISLabelStudio` thin WinForms frontend vs `OpenBISLabelStudio.Backend` class library owning `ILabelStudioBackend`, `openbis_fetch.py --worker`, `templates/*.lbx`, and runtime-only `config/*.json`), MSBuild build/publish commands (`OpenBISLabelStudio.Backend.dll`, `OpenBISLabelStudio.exe`, `Newtonsoft.Json.dll`, `openbis_fetch.py`, `templates\`), and reiterate that `config/` secrets are never tracked in Git or emitted by source publish.

- [ ] **Step 2: Run full MSBuild Debug + Release builds, C# tests (both Debug & Release), and `pytest`**

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" OpenBISLabelStudio.slnx /t:Restore,Build /p:Configuration=Debug /v:minimal
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" OpenBISLabelStudio.slnx /t:Restore,Build /p:Configuration=Release /v:minimal
.\OpenBISLabelStudio.Tests\bin\Debug\net10.0-windows\OpenBISLabelStudio.Tests.exe
.\OpenBISLabelStudio.Tests\bin\Release\net10.0-windows\OpenBISLabelStudio.Tests.exe
pytest tests/test_openbis_fetch.py -v
```
Expected: All builds exit 0; Debug & Release C# test runners print `0 failed`; `pytest` passes all tests.

- [ ] **Step 3: Verify Release Publish packaging & secret hygiene**

Run `/t:Publish` into a temporary folder and verify exact contents, clean `.csproj` boundary, and zero tracked secrets:
```powershell
$pubDir = Join-Path $env:TEMP ("openbis_publish_verify_" + [guid]::NewGuid().ToString("N"))
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild.exe" OpenBISLabelStudio\OpenBISLabelStudio.csproj /t:Publish /p:Configuration=Release /p:PublishDir="$pubDir\" /v:minimal
Get-ChildItem $pubDir -Recurse -Name
Remove-Item $pubDir -Recurse -Force
git ls-files | Select-String -Pattern '(^|/|\\)(config/|openbis_config\.json|openbis_types\.json|printers_config\.json)'
git grep -iE '"(openbisUrl|token)"\s*:\s*"[^"]+"'
Select-String -Path 'OpenBISLabelStudio\OpenBISLabelStudio.csproj' -Pattern 'bpac|Newtonsoft\.Json'
```
Expected:
- Publish folder contains `OpenBISLabelStudio.exe`, `OpenBISLabelStudio.Backend.dll`, `Newtonsoft.Json.dll`, `openbis_fetch.py`, and `templates\*.lbx`, and ZERO `config\`, `openbis_config.json`, `openbis_types.json`, or `printers_config.json`.
- `git ls-files` secret pattern check prints nothing.
- `git grep` secret key/value scan finds zero real URLs or tokens in tracked files.
- `OpenBISLabelStudio.csproj` grep for `bpac|Newtonsoft\.Json` prints nothing.

- [ ] **Step 4: Commit Task 5**

```bash
git add README.md
git commit -m "docs: update README for Backend/Frontend separation and verified publish structure"
```
