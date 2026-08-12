# Keysist — Capture-First Mappings Redesign

Date: 2026-08-12
Status: Approved for implementation
Supersedes (UI/setup portions of): `2026-08-11-keysist-design.md`
Keeps: Raw Input + LL-hook correlation engine, tray host, config location, autostart

## Problem

The routing engine already supports per-device key bindings, but the setup UI
does not match how users think about the product:

1. **Discover Device** keeps overwriting the detected key on every press
   (no Start/Stop capture).
2. Saving a device **requires a launch path**, even when the user wants
   Send Keys (e.g. Numpad1 → Ctrl+C). The placeholder claims the path can
   be blank; the validator contradicts that.
3. Trigger keys are chosen from a dropdown; actions are built as manual
   step rows. Users want to **press** the trigger and **press** the action.
4. Triggers are single-key only. Users need **chords** and **sequences**
   scoped to one physical keyboard.
5. `SendInputKeySender` taps each vkey as down+up independently, so a
   captured Ctrl+C cannot be replayed as a real chord without keydown/keyup
   support.

## Goals

- Capture-first mapping creation: Start → press → Stop (or auto-stop).
- Per-device triggers: same physical key on another keyboard is unaffected.
- Trigger modes: single key, held chord, ordered sequence.
- Action types in v1: send keys (with real chord replay), paste text
  (type vs clipboard+Ctrl+V), launch program/script.
- Hybrid device registration: pre-register by name, or auto-create from
  the first captured trigger.
- Action capture defaults to any keyboard; optional lock to one device.
- Publish Release builds to `dist/` at repo root.
- Keep existing config load path and hot-reload; migrate legacy
  single-key mappings without breaking them.

## Non-goals (this redesign)

- Macro scripting language / delays-heavy editor polish beyond what
  sendKeys steps already allow (delays remain in the step model for later).
- Mouse / non-keyboard HID.
- Cloud sync, multi-machine profiles.
- Non-Windows platforms.

## User experience

### Tray menu

- **Devices & Mappings** — main window
- **New Mapping…** — wizard
- **Register Device…** — name a device by pressing any key on it (no action)
- **Reload Config**
- **Exit**

Remove the old **Discover Device** menu item (replaced by Register Device
+ New Mapping).

### Main window

- Left: device list
- Right: mappings for the selected device (human-readable trigger + action summary)
- Buttons: New Mapping, Edit, Delete, Register Device
- Per-mapping swallow checkbox (default on; inherits device `defaultSwallow`
  when unset)

### New Mapping wizard

1. **Trigger**
   - Mode: Single key | Chord (held) | Sequence
   - Start Capture / Stop Capture
   - Optional: restrict to an already-registered device
   - On complete capture: show device identity + keys
   - If device unknown: prompt for a name and auto-register
2. **Action**
   - Send keys — capture chord/sequence (any keyboard by default; optional
     device lock), stored as sendKeys steps with keydown/keyup/vkey/text
   - Paste text — user enters string; method = `type` or `clipboard`
   - Launch — path + optional args + optional working directory
     (required only when this action type is selected)
3. **Options** — swallow on/off (default on) → Save

### Register Device

Start Capture → press any key on target device → Stop → enter name → Save.
No action required.

### Capture rules

- Capture is off until Start; Stop freezes the result (no further overwrites).
- Single key: first key-down completes (auto-stop) or Stop.
- Chord: track keys held on the filtered device; completing key is the
  non-modifier (or last) key while modifiers are down; Stop also accepted.
- Sequence: append key-downs until Stop (minimum 2 keys).
- While capturing, mapped actions for the capturing session do not fire
  (observe-only). Swallow remains governed by normal mappings outside
  capture; capture UI must not invent a second RawInputWindow.

## Architecture changes

### 1. Raw key-up events

`RawInputWindow` currently ignores break (key-up) flags. Expose:

- `RawKeyDown` (existing)
- `RawKeyUp` (new)

`InputPipeline` forwards both via pass-through events for capture UI and
uses downs/ups for per-device pressed-key state (chord matching).

### 2. Trigger model

Replace single-string-only matching with an explicit trigger:

```json
"trigger": {
  "mode": "key" | "chord" | "sequence",
  "keys": ["LControlKey", "NumPad1"]
}
```

Legacy `"key": "NumPad1"` remains readable: treated as
`{ "mode": "key", "keys": ["NumPad1"] }`. New saves write `trigger` and
may omit `key`, or keep `key` mirrored for the single-key case for
readability.

Matching:

- **key**: on key-down, device+vkey name equals the single key.
- **chord**: on key-down of a member key, all trigger keys are currently
  down on that same device (tracked via down/up). Fire once per chord
  activation (debounce on the completing key).
- **sequence**: rolling buffer of recent key-downs per device (TTL e.g.
  1000ms between keys); when the buffer suffix equals `keys`, fire and
  clear that match.

Swallow: for chords/sequences, eat the completing key (and ideally
suppress members when possible via the same correlator path). v1
requirement: at least the completing key is swallowed when swallow=true;
document that modifier leftovers may still reach apps in edge races
(same fail-safe as today: never eat without correlation).

### 3. CaptureSession service

Pure helper (testable) + thin UI wiring:

```csharp
enum CaptureMode { Key, Chord, Sequence }

class CaptureSession {
  void Start(CaptureMode mode, string? devicePathFilter);
  void Stop();
  bool IsCapturing { get; }
  CaptureResult? Result { get; } // devicePath, mode, keys[]
  void OnRawKeyDown(int vkey, string devicePath);
  void OnRawKeyUp(int vkey, string devicePath);
}
```

UI never creates a second Raw Input registration; it subscribes to
`InputPipeline` events and feeds the session. Pipeline exposes
`IsCaptureActive` (or a suppress callback) so dispatch skips while
capturing.

### 4. Action types

| Type | Config | Behavior |
|------|--------|----------|
| `launch` | path, args?, workingDirectory? | Process.Start (existing) |
| `sendKeys` | steps[] | SendInput (extended) |
| `pasteText` | text, method=`type`\|`clipboard` | type via unicode SendInput, or set clipboard + send Ctrl+V |

SendStep `send` values:

- `vkey` — key down then up (existing)
- `text` — unicode chars (existing)
- `keydown` — key down only (new)
- `keyup` — key up only (new)

Captured Ctrl+C becomes:

```json
"steps": [
  { "send": "keydown", "value": "LControlKey" },
  { "send": "vkey", "value": "C" },
  { "send": "keyup", "value": "LControlKey" }
]
```

(Equivalent RControl / ControlKey names accepted via `VirtualKeyNames`.)

Clipboard paste uses an injectable `IClipboard` for tests.

### 5. UI modules

| File | Role |
|------|------|
| `Ui/MainMappingsWindow.cs` | Replaces tree-only editor as primary window |
| `Ui/NewMappingWizard.cs` | 3-step wizard |
| `Ui/RegisterDeviceWindow.cs` | Device-only registration |
| `Ui/CaptureControl.cs` | Reusable Start/Stop + mode + result label |
| `Ui/ActionPickerPanel.cs` | Action type radios + panels |
| Remove/retire | `DiscoverDeviceWindow` (delete or thin-wrap to RegisterDevice) |
| Keep/adapt | `ActionEditorDialog` for Edit of existing launch/sendKeys/pasteText |

### 6. Publish

Release output directory: repo-root `dist/`.

```powershell
dotnet publish src/Keysist/Keysist.csproj -c Release -r win-x64 --self-contained false -o dist
```

Add `dist/` to `.gitignore`. Optional `publish.ps1` at repo root that runs
the command above. Existing `publish/` and `publish-fd/` artifacts are
legacy; do not commit them.

## Config schema (illustrative)

```json
{
  "devices": [
    {
      "name": "Numpad",
      "devicePathMatch": "HID#VID_1234&PID_5678",
      "defaultSwallow": true,
      "mappings": [
        {
          "trigger": { "mode": "key", "keys": ["NumPad1"] },
          "action": {
            "type": "sendKeys",
            "steps": [
              { "send": "keydown", "value": "LControlKey" },
              { "send": "vkey", "value": "C" },
              { "send": "keyup", "value": "LControlKey" }
            ]
          },
          "swallow": true
        },
        {
          "trigger": { "mode": "chord", "keys": ["LControlKey", "NumPad2"] },
          "action": {
            "type": "pasteText",
            "text": "hello",
            "method": "clipboard"
          }
        },
        {
          "trigger": { "mode": "sequence", "keys": ["NumPad7", "NumPad8"] },
          "action": {
            "type": "launch",
            "path": "C:\\\\Tools\\\\foo.exe"
          }
        }
      ]
    }
  ]
}
```

`anyKeyAction` remains supported as today.

## Data flow (unchanged spine)

```
physical keypress
  -> WM_INPUT (device + vkey, down/up)
  -> CaptureSession? (if active: record, suppress dispatch)
  -> pressed-key state per device
  -> trigger match (key | chord | sequence)
  -> WH_KEYBOARD_LL correlator for swallow
  -> ActionDispatcher (launch | sendKeys | pasteText)
```

## Error handling

- Same fail-safes as v1: never eat without correlation; bad config keeps
  last-good; launch failures balloon + log.
- Capture with no result on Save → validation message.
- Launch action without path → validation message (only for launch type).
- Unknown paste method → treat as error at dispatch (log, no crash).

## Testing

Automated (extend existing xUnit suite):

- CaptureSession: start/stop, single/chord/sequence, device filter, no
  overwrite after stop
- TriggerMatcher: key/chord/sequence + legacy `key` field
- ActionDispatcher: pasteText type + clipboard; sendKeys keydown/keyup
- Config serialization: trigger + pasteText round-trip; legacy key still loads

Manual on user machine:

- Two keyboards: Numpad1 on A → Ctrl+C; Numpad1 on B types normally
- Capture Start/Stop does not keep changing after Stop
- New Mapping with Send keys does not ask for launch path
- Publish lands runnable `Keysist.exe` under `dist/`

## Success criteria

1. User can bind one key on one device to Ctrl+C via capture, without
   typing a launch path.
2. Same key on a second keyboard is unchanged.
3. Chord and sequence triggers work on a single device.
4. `dotnet publish ... -o dist` produces a runnable build; `dist/` is gitignored.
