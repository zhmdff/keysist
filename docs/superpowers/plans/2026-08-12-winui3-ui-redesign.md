# WinUI 3 UI Redesign Implementation Plan

> **For agentic workers:** Use superpowers:subagent-driven-development. Prefer file briefs (`task-N-brief.md`) over pasting this whole plan into prompts. Keep implementer reports in `.superpowers/sdd/`. Checkbox tracking below.

**Goal:** Split Core from UI; ship redesigned WinUI 3 unpackaged hub + WinForms tray shim; preserve remapper behavior; publish to `dist/`.

**Architecture:** `Keysist.Core` (engine) + `Keysist` (WinUI app + tray). Tests → Core only.

**Tech Stack:** .NET 8, Windows App SDK (unpackaged), WinUI 3, WinForms NotifyIcon, existing xUnit suite.

## Global Constraints

- Unpackaged (`WindowsPackageType=None`); MSIX out of scope.
- Tray = WinForms `NotifyIcon` only; all other UI = WinUI 3.
- Core must not reference WinUI/WinForms (except if a types-only need appears — avoid).
- No second Raw Input registration; capture uses `InputPipeline` events.
- Keep config path, autostart, single-instance mutex, `dist/` publish.
- Token-friendly execution: one task per subagent; brief file + report file; no full-plan paste.
- Commit after each task.

## File structure (target)

| Path | Role |
|------|------|
| `src/Keysist.Core/**` | Moved engine code |
| `src/Keysist/App.xaml(+.cs)` | WinUI app entry |
| `src/Keysist/MainWindow.xaml(+.cs)` | Hub shell + NavigationView |
| `src/Keysist/Views/DevicesPage.xaml(+.cs)` | Device list |
| `src/Keysist/Views/MappingsPage.xaml(+.cs)` | Mapping list/cards |
| `src/Keysist/Views/NewMappingPage.xaml(+.cs)` | 3-step create flow |
| `src/Keysist/Views/RegisterDevicePage.xaml(+.cs)` | Register device |
| `src/Keysist/Controls/CapturePanel.xaml(+.cs)` | Start/Stop capture |
| `src/Keysist/Tray/TrayIconController.cs` | WinForms NotifyIcon |
| `src/Keysist/Services/KeysistWorkspace.cs` | Holds ConfigStore + Pipeline |
| `tests/Keysist.Tests/*` | Project ref → Core |
| Delete | Old `Ui/*` WinForms windows after parity |

---

### Task 1: Extract Keysist.Core

**Files:** Create `src/Keysist.Core/Keysist.Core.csproj`; move non-UI folders from `src/Keysist` (Config, Pipeline, Capture, Actions, Hooking, RawInputInterop, Correlation, Startup, Diagnostics); update namespaces if needed (`Keysist.*` OK); point tests at Core; leave a temporary thin WinForms host compiling OR keep moving until Task 2 scaffolds WinUI.

**Steps:**
- [ ] Create Core csproj (`net8.0-windows`, WinForms false, no WinUI)
- [ ] Move engine sources; fix project refs
- [ ] `dotnet test` — all pass
- [ ] Commit: `refactor: extract Keysist.Core engine library`

---

### Task 2: Scaffold WinUI unpackaged app + tray shim

**Files:** Convert/replace `src/Keysist` to WinUI 3 unpackaged app referencing Core; `App.xaml`, minimal `MainWindow`; `TrayIconController` using WinForms NotifyIcon; `Program`/`App` startup: mutex → workspace → pipeline → tray → `Application.Start`.

**Requirements:**
- `WindowsPackageType=None`
- `UseWindowsForms=true` for tray only
- Tray: Open Keysist / New Mapping / Register Device / Reload / Exit
- Opening windows: `DispatcherQueue.TryEnqueue` to show WinUI window
- Exit: stop pipeline, exit app

**Steps:**
- [ ] Scaffold WinUI project (or migrate csproj)
- [ ] Wire workspace + tray + blank MainWindow
- [ ] `dotnet build` succeeds
- [ ] Commit: `feat: scaffold WinUI unpackaged app with tray host`

---

### Task 3: Hub shell — Devices + Mappings pages

**Files:** `MainWindow` NavigationView; `DevicesPage`; `MappingsPage`; bind to `ConfigStore`.

**UX:**
- Devices: ListView of names; selection filters mappings
- Mappings: show trigger summary + action summary; Swallow ToggleSwitch; Delete mapping; Delete device (confirm)
- Empty states with CTA buttons
- New Mapping / Register Device navigate to those pages (stubs OK if Task 4 fills them)

**Steps:**
- [ ] Implement shell + two pages with live config data
- [ ] Delete device / delete mapping / swallow persist via ConfigStore
- [ ] Build OK
- [ ] Commit: `feat: WinUI hub with devices and mappings pages`

---

### Task 4: CapturePanel + Register Device + New Mapping flow

**Files:** `CapturePanel`; `RegisterDevicePage`; `NewMappingPage` (3 steps).

**Behavior:** Match capture-first spec — Start/Stop, modes, `IsCaptureActive`, auto-register device name, actions sendKeys/pasteText/launch, swallow on review step. Reuse Core `CaptureSession` + pipeline events; marshal to UI thread.

**Steps:**
- [ ] CapturePanel wraps CaptureSession
- [ ] Register Device end-to-end
- [ ] New Mapping 3-step end-to-end
- [ ] Edit action (ContentDialog or page) for existing mapping
- [ ] Build + test suite
- [ ] Commit: `feat: WinUI capture, register, and new-mapping flows`

---

### Task 5: Remove WinForms UI leftovers + publish

**Files:** Delete obsolete WinForms UI; update `publish.ps1` for WinUI unpackaged publish; smoke `dist\Keysist.exe`.

**Publish sketch:**
```powershell
dotnet publish src/Keysist/Keysist.csproj -c Release -r win-x64 --self-contained false -o dist
```
(Adjust for WinAppSDK unpackaged as required by SDK version.)

**Steps:**
- [ ] Delete dead WinForms windows
- [ ] Update publish script; run it
- [ ] Smoke: tray opens hub; create mapping; exit clean
- [ ] Commit: `chore: drop WinForms UI; publish WinUI build to dist`

---

## Spec coverage

| Spec item | Task |
|-----------|------|
| Core split | 1 |
| WinUI unpackaged + tray shim | 2 |
| Hub IA Devices/Mappings | 3 |
| Capture + New Mapping + Register | 4 |
| Delete old UI + dist | 5 |
| Engine behavior preserved | 1–4 (no Core logic rewrites beyond moves) |

## Token-friendly execution notes

- Controller: extract brief per task; implementer returns short status + report path
- Prefer `composer-2.5` / fast models for scaffolding; mid-tier for WinUI XAML UX judgment
- Do not paste prior task histories into later prompts
- Visual polish: follow spec visual direction; no purple-gradient template look
