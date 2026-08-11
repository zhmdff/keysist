# Keysist — Per-Device Key/Input Router — Design Spec

Date: 2026-08-11
Status: Approved (pending user spec review)

## Problem

User has multiple keyboard-like input devices (a remote control, a numpad-only
keyboard) attached to the same Windows PC. Standard hotkey tools (AHK
`Hotkey`, global keyboard hooks) see only *which key* was pressed, not *which
physical device* sent it — a `Numpad5` from the numpad-only keyboard is
indistinguishable from `Numpad5` on the main keyboard. The user wants
bindings scoped to a specific *device*, with per-key mapping as the default
mode and "any key on this device" as an option.

## Goals

- Assign actions (launch app/script, or send remapped keys/macros) to
  specific physical devices.
- Default: per-key, device-scoped — same key on a different device is
  unaffected.
- Bindings must not affect the same physical key when pressed on a device
  that isn't mapped (e.g. main keyboard's real Numpad5 keeps working
  normally).
- Default behavior swallows the original keystroke when a mapping fires;
  each mapping can opt into passthrough (original keystroke still reaches
  the focused app in addition to the action).
- Discover/identify devices via the app itself (no external HID tooling
  required).
- Runs continuously in background (tray), autostarts with Windows.
- Config editable via a GUI (tray → editor window), not hand-written JSON.

## Non-goals

- No support for non-Windows platforms.
- No cloud sync / multi-machine config.
- No support for devices that don't emit standard HID keyboard/generic HID
  reports (e.g. proprietary vendor SDKs) in v1.
- No advanced macro scripting language — sequences are an ordered list of
  key-sends/delays, not a full scripting DSL.

## Architecture

Single C# (.NET 8, Windows) tray application, `Keysist.exe`, always running
in background once started (registered in `HKCU\...\Run` for autostart).

### 1. Raw Input listener

- Hidden message-only window (`HWND_MESSAGE`) receiving `WM_INPUT`.
- Registers via `RegisterRawInputDevices` for:
  - Usage Page 1 / Usage 6 (generic keyboard)
  - Usage Page 1 / Usage 2 (mouse) — only if a future need arises; not
    wired up in v1 beyond the registration groundwork.
- Flag `RIDEV_INPUTSINK` so input is captured regardless of foreground
  focus.
- Each `WM_INPUT` gives `hDevice` (session-scoped handle). Resolved via
  `GetRawInputDeviceInfo` (`RIDI_DEVICENAME`) to the HID device path, e.g.
  `\\?\HID#VID_1234&PID_5678&...`. VID/PID substring extracted from this
  path for display purposes; the full path (or a stable subset — container
  ID is not guaranteed stable across USB replug for all devices, so path
  matching is done on VID+PID+first N path segments, confirmed empirically
  during device discovery) is the persisted device identity key.

### 2. Low-level keyboard hook (swallow capability)

- Raw Input cannot block delivery to other applications — only observe.
- A `WH_KEYBOARD_LL` hook is installed to actually eat keystrokes when a
  mapping's swallow flag is true.
- The LL hook callback does not carry device identity, only vkey/scancode
  and timestamp. Correlation strategy: the Raw Input handler, on receiving
  a keydown, pushes `(vkey, timestampMs)` onto a short-lived pending queue
  (few-ms TTL). The LL hook callback, on the matching vkey within that
  window, looks up whether *any* pending Raw Input event for that vkey maps
  to a device+key combo requiring swallow; if so, returns 1 (eat), else
  calls `CallNextHookEx` (pass through). Entries expire after ~15ms to
  avoid stale matches if Raw Input delivery is delayed or lost.
- If Raw Input a given key ever fails to correlate (e.g. dropped message),
  the LL hook defaults to passthrough — never eats without a confirmed
  device+mapping match. Prioritizes "never break normal typing" over
  "always enforce swallow."

### 3. Action dispatcher

Two action kinds:

- **Launch**: `Process.Start` with configurable path, arguments, working
  directory.
- **SendKeys**: ordered list of `{ send: "vkey"|"text", value, delayMs }`
  steps executed via `SendInput`, allowing remapped single keys, key
  combos, or short macros.

Dispatch happens on a background thread, decoupled from the hook/raw-input
callbacks (which must return quickly to avoid Windows unhooking a slow LL
hook).

### 4. Config

- Location: `%AppData%\Keysist\config.json`.
- Loaded at startup and hot-reloaded via `FileSystemWatcher` (debounced
  ~300ms to avoid partial-write reads).
- On parse failure: keep last-good in-memory config, show a tray balloon
  warning with the error, do not crash.
- Schema (illustrative):

```json
{
  "devices": [
    {
      "name": "Numpad",
      "devicePathMatch": "HID#VID_1234&PID_5678",
      "defaultSwallow": true,
      "mappings": [
        {
          "key": "Numpad5",
          "action": { "type": "launch", "path": "C:\\Tools\\foo.exe", "args": "" },
          "swallow": true
        },
        {
          "key": "Numpad7",
          "action": { "type": "sendKeys", "steps": [ { "send": "vkey", "value": "VK_MEDIA_PLAY_PAUSE" } ] },
          "swallow": false
        }
      ]
    },
    {
      "name": "Remote",
      "devicePathMatch": "HID#VID_9999&PID_0001",
      "defaultSwallow": true,
      "anyKeyAction": { "type": "launch", "path": "C:\\Tools\\bar.exe" }
    }
  ]
}
```

- `mappings` = per-key mode (default). `anyKeyAction` = device-only mode
  (any key on this device triggers one action, key identity ignored) — set
  instead of `mappings` for a device that wants that behavior.
- `swallow` on each mapping overrides `defaultSwallow` for that entry.

### 5. GUI (tray + editor window)

- Tray icon context menu: **Edit Mappings**, **Discover Device**,
  **Reload Config**, **Exit**.
- **Discover Device** mode: reuses the Raw Input pipe in "observe only"
  mode. User presses a key on the target device; app shows resolved
  device identity (VID/PID, resolved friendly name if Windows exposes one)
  and the key pressed. User names the device and, inline, assigns/edits
  its action(s) — writes back to `config.json`.
- **Edit Mappings** window: list of configured devices, each expandable to
  its mappings; add/edit/delete mapping rows (key picker reuses discover
  mode to capture a key press instead of a dropdown, since scancodes for
  device-specific keys aren't always standard); action editor for
  launch vs sendKeys; swallow toggle per mapping.
- No hand-editing of JSON required for normal use; the file remains
  directly editable for power users/backup/versioning.

## Data flow

```
physical keypress
  -> HID driver
  -> WM_INPUT (device handle + vkey)  \
                                        > correlated within ~15ms window
  -> WH_KEYBOARD_LL (vkey + eat-flag) /
  -> lookup (devicePathMatch, vkey) in current config
       match:  run action (async) ; eat or passthrough per mapping.swallow
       no match: passthrough untouched (unmapped device or unmapped key)
```

## Error handling

- Unknown device or unmapped key on a known device: always passthrough,
  never blocks normal input.
- Config parse error: keep last-good config, tray balloon warning.
- Action launch failure (e.g. bad path): tray balloon with error message;
  listener keeps running.
- LL hook must return within Windows' hook timeout (~implicit, avoid doing
  blocking work in the callback) — all actual dispatch work is queued to a
  background thread.

## Testing (manual, no automated test suite planned for v1)

- Discover Device mode correctly resolves distinct VID/PID for numpad vs
  main keyboard vs remote.
- Device path match remains stable after unplug/replug and after reboot.
- Numpad5 from mapped numpad triggers action; Numpad5 from main keyboard
  types normally (mapping does not leak to other devices).
- Swallow=true mapping does not produce the original keystroke in a text
  editor; swallow=false mapping produces both the action and the original
  keystroke.
- Config hot-reload picks up edits without restarting the app.
- Invalid config.json does not crash the app and shows a warning.

## Open items validated during implementation (not blocking design approval)

- Exact stability of device path across replug (validate empirically in
  Discover Device mode notes during first device pairing).
- Whether the remote control enumerates as a keyboard-usage HID or a
  vendor-defined/generic HID collection — Discover Device mode must handle
  both (generic HID usage pages beyond keyboard, if the remote doesn't
  show up under usage page 1/6, will need registering an additional
  RawInputDevice usage page — flagged for first real-device test).
