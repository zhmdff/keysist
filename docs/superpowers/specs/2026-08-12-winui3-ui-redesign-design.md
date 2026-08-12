# Keysist — WinUI 3 UI Redesign

Date: 2026-08-12  
Status: Approved for planning  
Depends on: capture-first engine (`2026-08-12-capture-first-mappings-design.md`)

## Goal

Replace WinForms UI with **WinUI 3** (unpackaged), keep the remapper engine, and ship a clearer UX for device/mapping setup. MSIX later.

## Decisions (locked)

| Topic | Choice |
|-------|--------|
| Architecture | Split: `Keysist.Core` + WinUI `Keysist` app |
| Packaging | Unpackaged now; MSIX later |
| Tray | Thin WinForms `NotifyIcon` only |
| Scope | Full UI/UX redesign (not a 1:1 control port) |
| Engine | No behavior change to Raw Input / hook / config schema |

## Non-goals

- MSIX / Store packaging (phase 2)
- Mouse capture/actions
- Rewriting Core pipeline logic except UI-facing facades
- Pure-WinUI tray (no good built-in)

## Project structure

```
src/Keysist.Core/     # class lib: Config, Pipeline, Capture, Actions, Hooking, RawInput, Startup, Diagnostics
src/Keysist/          # WinUI 3 unpackaged app + Tray/ (WinForms NotifyIcon shim)
tests/Keysist.Tests/  # references Core only
```

- Target: `net8.0-windows10.0.19041.0` (or current WinAppSDK baseline)
- App: Windows App SDK, unpackaged (`WindowsPackageType=None`)
- `UseWindowsForms=true` only for tray shim
- Remove unused `UseWPF` unless WinAppSDK requires it (prefer off)

## Process model

Unchanged: single-instance mutex → load config → start `InputPipeline` → show tray → no window until menu → Exit stops pipeline.

Publish: `publish.ps1` → `dist/` (WinUI unpackaged publish flags as needed).

## UX redesign

### Principles

1. **One hub window** — primary surface is a single main window, not a pile of modal dialogs.
2. **Capture is obvious** — large Start/Stop, live result, device badge.
3. **Mappings are scannable** — trigger → action in plain language; swallow as a clear toggle.
4. **Guided create** — New Mapping is a 3-step flow inside the hub (or a single NavigationView page stack), not a cramped classic wizard.
5. **Empty states** — first-run tells you to Register Device or New Mapping.

### Information architecture

**Tray menu**

- Open Keysist (main window)
- New Mapping (opens hub on create flow)
- Register Device
- Reload Config
- Exit

**Main window (`MainWindow`)**

- Left nav (`NavigationView`): **Devices** · **Mappings** (all) · optional **Settings** (autostart note later)
- **Devices** page: list with name + path match; select → mappings for that device; Register; Delete device
- **Mappings** page (device-filtered when a device is selected): list/cards; New; Edit action; Delete mapping; Swallow toggle
- **New Mapping** flow (ContentDialog or dedicated page):  
  1. Capture trigger (mode + Start/Stop + optional device filter + auto-register name)  
  2. Choose action (Send keys capture / Paste text / Launch)  
  3. Review (swallow + summary) → Save
- **Register Device**: compact capture + name → Save
- **Edit**: edit action fields; trigger shown read-only (re-capture later if needed)

### Visual direction

- System light/dark (`RequestedTheme` follow system)
- Look: cool slate surfaces, one accent (amber/gold) for “capturing” and primary CTAs — avoid purple-gradient AI defaults
- Typography: Segoe UI Variable (WinUI default) with clear hierarchy; no decorative font packs required
- Density: comfortable; cards only for interactive mapping rows
- Motion: short navigation transitions; capture pulse while listening (2–3 intentional motions max)

### Capture UX rules (same engine semantics)

- Start/Stop; no overwrite after Stop
- Modes: Key / Chord / Sequence
- Suppress dispatch while capturing (`IsCaptureActive`)
- Single Raw Input registration via Core pipeline

## UI ↔ Core boundary

WinUI code may depend on Core. Core must not depend on WinUI/WinForms.

Optional thin facade in app:

```csharp
public interface IKeysistWorkspace
{
    ConfigStore Config { get; }
    InputPipeline Pipeline { get; }
    void ReloadConfig();
}
```

Windows resolve services from `App` static/DI.

## Migration steps (high level)

1. Extract `Keysist.Core`; move non-UI code; fix tests.
2. Scaffold WinUI unpackaged app; wire Core; blank main window + tray shim.
3. Implement hub shell + Devices/Mappings pages.
4. Port capture control + New Mapping / Register flows with new UX.
5. Delete old WinForms UI types; update publish.
6. Manual smoke: tray, capture, swallow, two keyboards, `dist\` exe.

## Success criteria

1. No WinForms UI except tray `NotifyIcon`.
2. All current mapping flows work in WinUI hub.
3. UX: create a NumPad→paste mapping without launch-path prompts or endless capture.
4. `dotnet test` green; `publish.ps1` produces runnable unpackaged `dist\Keysist.exe`.
5. Core unchanged in observable remapping behavior.

## Risks

| Risk | Mitigation |
|------|------------|
| WinUI + WinForms tray apartment/dispatch | Create WinUI app first; open windows via `DispatcherQueue` from tray clicks |
| Unpackaged WinAppSDK deploy | Follow official unpackaged bootstrap; document runtime requirement if any |
| Scope creep in “full redesign” | Hub + create/register/edit only; no Settings cloud, no themes marketplace |
