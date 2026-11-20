# Custom App Icon (Option 2) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the Option 2 custom **Flask + Sample Tag** icon to `OpenBISLabelStudio.exe` (`<ApplicationIcon>`) and to both `Form1` and `SetupSettingsForm` windows via an embedded resource helper.

**Architecture:** Save canonical vector art at `OpenBISLabelStudio/Assets/app-icon.svg` and a supersampled multi-resolution (`16`, `24`, `32`, `48`, `64`, `256` px) Windows icon at `OpenBISLabelStudio/Assets/app-icon.ico`. Configure `<ApplicationIcon>` and `<EmbeddedResource>` in `OpenBISLabelStudio.csproj`, add an `internal static class AppIconProvider` in `OpenBISLabelStudio` to instantiate owned window icons per form, apply and cleanly dispose them on `Form1` and `SetupSettingsForm`, and verify via UI/resource tests.

**Tech Stack:** Python 3.14 + Pillow 13 (asset generation), .NET 10 Windows Forms (`net10.0-windows`), MSBuild 18 (`C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe`).

**Spec:** [2026-11-20-custom-app-icon-design.md](file:///c:/Users/dafa/Documents/git/openbis-label-studio/docs/superpowers/specs/2026-11-20-custom-app-icon-design.md)

## Global Constraints

- Do not set an icon on `OpenBISLabelStudio.Tests.exe` or `OpenBISLabelStudio.Backend.dll`.
- Keep `AppIconProvider` inside `OpenBISLabelStudio` (not `Backend`) and respect `FrontendFormsContainNoBackendServiceOrSecretReferences`.
- Load the icon from managed `EmbeddedResource` (`OpenBISLabelStudio.Assets.app-icon.ico`), never relying solely on `Application.ExecutablePath`.
- Dispose each form's owned `Icon` instance cleanly on form disposal without holding dead handles during HWND teardown.

## Review Focus

1. **`.NET System.Drawing.Icon` compatibility with multi-frame `.ico`:** `new Icon(stream)` and `new Icon(icon, 16, 16)` / `(32, 32)` must load cleanly on Windows `.NET 10` without `ArgumentException` from any compressed PNG/BMP ICO directory entry.
2. **GDI handle ownership & disposal safety:** Closing/disposing `Form1` and `SetupSettingsForm` multiple times must clear `Icon = null` before disposing the owned `Icon` (or dispose after `base.Dispose`) without `ObjectDisposedException` or Win32 handle leaks.
3. **Frontend assembly & isolation rule:** `AppIconProvider` must remain free of `Backend.Services`, `Models`, `Newtonsoft.Json`, or `bpac` references (`FrontendFormsContainNoBackendServiceOrSecretReferences`).
4. **Release & published `.exe` Win32 icon embedding:** `<ApplicationIcon>Assets\app-icon.ico</ApplicationIcon>` must embed cleanly into `OpenBISLabelStudio.exe` (`Icon.ExtractAssociatedIcon` succeeds on compiled `.exe`).
5. **Graceful missing-resource fallback:** If the embedded resource stream is unavailable or corrupt, `AppIconProvider.TryCreateWindowIcon()` returns `null` without crashing startup.

---

### Task 1: Generate Option 2 Assets, Wire `.csproj` & Forms, and Verify Window Icon Tests

**Files:**
- Create: `OpenBISLabelStudio/Assets/app-icon.svg`
- Create: `OpenBISLabelStudio/Assets/app-icon.ico`
- Create: `OpenBISLabelStudio/AppIconProvider.cs`
- Modify: `OpenBISLabelStudio/OpenBISLabelStudio.csproj:3-15`
- Modify: `OpenBISLabelStudio/Form1.cs:25-70, 125-145`
- Modify: `OpenBISLabelStudio/SetupSettingsForm.cs:35-82, 220-235`
- Test: `OpenBISLabelStudio.Tests/Program.cs`

**Interfaces:**
- Produces: `internal static class AppIconProvider` (`TryCreateWindowIcon() -> Icon?`, `AttachWindowIcon(Form form, ref Icon? ownedIcon)`, `DetachWindowIcon(Form form, ref Icon? ownedIcon)`).

- [ ] **Step 1: Write the failing icon resource and Form icon test in `OpenBISLabelStudio.Tests/Program.cs`**

Register `AppIconProviderLoadsMultiResolutionWindowIconAndAppliesToForms` in `Main`, verifying:
  - `AppIconProvider.TryCreateWindowIcon()` returns non-null `Icon` with `Width > 0` and `Height > 0`, and `new Icon(icon, 16, 16)` + `new Icon(icon, 32, 32)` succeed cleanly.
  - `Form1` (`new Form1(stub)`) and `SetupSettingsForm` (`new SetupSettingsForm(stub, SetupDialogMode.Settings)`) both have non-null `Icon` instances distinct from `SystemIcons.Application`, and repeating create/show/dispose cycles leaves `form.Icon == null` after disposal cleanly without exceptions.
  - Note: Check `Icon.ExtractAssociatedIcon` on `typeof(Form1).Assembly.Location.Replace(".dll", ".exe")` when `.exe` exists on disk.

- [ ] **Step 2: Run Release build to verify test fails before implementation**

Run: `& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" OpenBISLabelStudio.slnx /t:Build /p:Configuration=Release /v:minimal`
Expected: FAIL with compile error (`AppIconProvider` does not exist).

- [ ] **Step 3: Create `OpenBISLabelStudio/Assets/app-icon.svg` and multi-size `OpenBISLabelStudio/Assets/app-icon.ico` (Option 2)**

Write `app-icon.svg` with the clean Option 2 vector geometry (`#0f766e` -> `#0d9488` rounded square, white Erlenmeyer flask, `#5eead4` liquid, dark navy `#0f172a` corner QR label card) and generate `app-icon.ico` via Python Pillow (`1024×1024` supersampled RGBA downsampled via `Image.Resampling.LANCZOS` with frames `[256, 64, 48, 32, 24, 16]`).

- [ ] **Step 4: Wire `.csproj`, `AppIconProvider.cs`, `Form1.cs`, and `SetupSettingsForm.cs`**

- Update `OpenBISLabelStudio/OpenBISLabelStudio.csproj`: add `<ApplicationIcon>Assets\app-icon.ico</ApplicationIcon>` in `<PropertyGroup>` and `<EmbeddedResource Include="Assets\app-icon.ico" />` in an `<ItemGroup>`.
- Create `OpenBISLabelStudio/AppIconProvider.cs` loading `"OpenBISLabelStudio.Assets.app-icon.ico"` from `typeof(AppIconProvider).Assembly.GetManifestResourceStream(...)` cleanly inside `try/catch`, returning `null` safely if missing/corrupt, plus `Apply` / `Release` helpers.
- Update `Form1.cs` and `SetupSettingsForm.cs` to attach `_windowIcon` after `InitializeComponent()` and detach/dispose it cleanly on form disposal.

- [ ] **Step 5: Run verification suite (`Release` build, `Release` C# tests, `pytest`, and `Debug` if unlocked)**

Run MSBuild `Release` (and `Debug` if unlocked by user), `OpenBISLabelStudio.Tests.exe` (Release, plus Debug if rebuilt), and `pytest tests/test_openbis_fetch.py -v`.
Expected: All executed builds/tests pass with `0 failed`.

- [ ] **Step 6: Commit Task 1**

```bash
git add OpenBISLabelStudio/Assets/app-icon.svg OpenBISLabelStudio/Assets/app-icon.ico OpenBISLabelStudio/AppIconProvider.cs OpenBISLabelStudio/OpenBISLabelStudio.csproj OpenBISLabelStudio/Form1.cs OpenBISLabelStudio/SetupSettingsForm.cs OpenBISLabelStudio.Tests/Program.cs
git commit -m "feat: add custom Option 2 app icon to executable and forms"
```
