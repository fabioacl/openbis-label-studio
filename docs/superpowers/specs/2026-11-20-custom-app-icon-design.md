# Design: Custom App Icon (Option 2 — Flask + Sample Tag)

**Date:** 2026-11-20
**Branch:** `feat/custom-app-icon` (off `main`)
**Scope:** Small change (Option 2 multi-resolution `.ico` + `.svg` asset, `.csproj` `<ApplicationIcon>`, WinForms `Icon` assignment helper, unit tests)
**Status:** Pending User Approval

---

## 1. Intent & Why

Today `OpenBISLabelStudio` uses the default generic Windows executable and WinForms window icon (`OpenBISLabelStudio.csproj` sets no `<ApplicationIcon>` and `Form1`/`SetupSettingsForm` set no `Icon`). Giving the app a distinctive laboratory-label icon makes it immediately recognizable in Windows Explorer, desktop shortcuts, taskbar tabs, Alt+Tab, and window title bars.

## 2. Goals, Non-Goals, and Anti-Goals

- **Goals:**
  - Create **Option 2 (Flask + Sample Tag)**: deep teal rounded tile (`#0f766e` -> `#0d9488`) with a clean white Erlenmeyer flask, `#5eead4` liquid fill, and dark-navy `#0f172a` corner QR label badge.
  - Provide crisp multi-resolution `.ico` frames (`16`, `24`, `32`, `48`, `64`, `256` px) at `OpenBISLabelStudio/Assets/app-icon.ico` alongside source vector `OpenBISLabelStudio/Assets/app-icon.svg`.
  - Wire `<ApplicationIcon>Assets\app-icon.ico</ApplicationIcon>` in `OpenBISLabelStudio/OpenBISLabelStudio.csproj` for the compiled executable icon.
  - Embed `Assets\app-icon.ico` cleanly as an `EmbeddedResource` loaded via a tiny `AppIconProvider` helper in `OpenBISLabelStudio` so `Form1`, `SetupSettingsForm`, and UI unit tests all display and own their window `Icon` reliably and dispose it without handle leaks.
- **Non-Goals:**
  - Adding an executable icon to `OpenBISLabelStudio.Tests.exe` or `OpenBISLabelStudio.Backend.dll`, runtime theme-switching icons, or external `.ico` files loose beside config folders.
- **Anti-Goals:**
  - Never rely solely on `Application.ExecutablePath` for Form icons (fails inside test runners and single-file hosts), never leave un-disposed GDI `Icon` handles across form close/reopen, and never violate `FrontendFormsContainNoBackendServiceOrSecretReferences`.

## 3. Approaches Considered

1. **Multi-Resolution `.ico` (`ApplicationIcon` + `EmbeddedResource`) + Form Helper (Proposed):**
   Embeds `Assets\app-icon.ico` in both `OpenBISLabelStudio.exe` native Win32 resources (`<ApplicationIcon>`) and managed manifest resources (`<EmbeddedResource>`), applied per form via `AppIconProvider.TryCreateWindowIcon()` and disposed cleanly when each form closes. Works identically in Explorer, taskbar, Alt+Tab, Debug/Release runs, and `OpenBISLabelStudio.Tests.exe`.
2. **Runtime `Icon.ExtractAssociatedIcon(Application.ExecutablePath)` Only (Rejected):**
   Couples window title-bar icon to the host `.exe` path (shows the test runner icon during tests and depends on disk extraction).

## 4. Verification Plan

- Add unit/UI checks in `OpenBISLabelStudio.Tests/Program.cs` verifying `AppIconProvider.TryCreateWindowIcon()` returns a valid multi-frame icon (non-empty dimensions), `Form1` and `SetupSettingsForm` both receive non-null custom `Icon` instances without throwing, closing/disposing them cleans up without GDI errors, and `FrontendFormsContainNoBackendServiceOrSecretReferences` passes.
- Verify Debug & Release MSBuild builds (`OpenBISLabelStudio.slnx /t:Restore,Build`), `OpenBISLabelStudio.Tests.exe` (Debug & Release, if unlocked — noting whether user Debug instances lock `bin\Debug\`), and `pytest tests/test_openbis_fetch.py -v`.
