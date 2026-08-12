# Capture-First Mappings Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace Keysist's awkward Discover/dropdown setup with Start/Stop capture, chord/sequence triggers, pasteText actions, and publish Release builds to `dist/`.

**Architecture:** Keep Raw Input + LL-hook correlation. Add key-up events, a testable `CaptureSession`, a `TriggerMatcher` with per-device pressed state, extend sendKeys with keydown/keyup, add pasteText, and rebuild the WinForms UI around a wizard + main mappings window.

**Tech Stack:** .NET 8, WinForms, xUnit, existing Keysist project layout under `src/Keysist` and `tests/Keysist.Tests`.

## Global Constraints

- Windows-only (`net8.0-windows`); do not add cross-platform abstractions.
- Never create a second `RawInputWindow` / `RegisterRawInputDevices` call — UI observes via `InputPipeline` events only.
- Never eat a keystroke without correlator confirmation (fail-safe passthrough).
- While capture is active, suppress action dispatch for the capturing pipeline.
- Legacy `"key": "NumPad1"` mappings must keep loading and matching.
- Launch path is required only when action type is `launch`.
- Release publish output is repo-root `dist/`; `dist/` must be gitignored.
- Follow TDD for non-UI tasks; UI tasks get focused build verification.
- Commit after each task.

## File structure

| Path | Responsibility |
|------|----------------|
| `src/Keysist/Config/ConfigModels.cs` | TriggerConfig, pasteText fields, KeyMapping.Trigger |
| `src/Keysist/Config/TriggerMatcher.cs` | key/chord/sequence match + pressed-key state |
| `src/Keysist/Capture/CaptureSession.cs` | Start/Stop capture logic (no UI) |
| `src/Keysist/RawInputInterop/RawInputWindow.cs` | Add RawKeyUp |
| `src/Keysist/Pipeline/InputPipeline.cs` | Forward ups, capture suppress, use TriggerMatcher |
| `src/Keysist/Actions/VirtualKeyNames.cs` | Resolve Keys enum names + media aliases |
| `src/Keysist/Actions/SendInputKeySender.cs` | keydown/keyup |
| `src/Keysist/Actions/IClipboard.cs` + `WindowsClipboard.cs` | Clipboard for pasteText |
| `src/Keysist/Actions/ActionDispatcher.cs` | pasteText dispatch |
| `src/Keysist/Ui/CaptureControl.cs` | Reusable Start/Stop control |
| `src/Keysist/Ui/RegisterDeviceWindow.cs` | Device registration |
| `src/Keysist/Ui/NewMappingWizard.cs` | 3-step wizard |
| `src/Keysist/Ui/MainMappingsWindow.cs` | Primary editor |
| `src/Keysist/Tray/TrayIconController.cs` | New menu items |
| Delete: `src/Keysist/Ui/DiscoverDeviceWindow.cs` | Replaced |
| Adapt or delete: `EditMappingsWindow.cs` | Replaced by MainMappingsWindow |
| `publish.ps1` | Publish to dist |
| `.gitignore` | Add dist/ |
| Tests under `tests/Keysist.Tests/` | Mirror new types |

---

### Task 1: Config models — Trigger + pasteText + legacy key

**Files:**
- Modify: `src/Keysist/Config/ConfigModels.cs`
- Modify: `tests/Keysist.Tests/ConfigModelsSerializationTests.cs`
- Create: `tests/Keysist.Tests/KeyMappingTriggerTests.cs`

**Interfaces:**
- Produces: `TriggerConfig { string Mode; List<string> Keys }`, `KeyMapping.Trigger`, `KeyMapping.GetEffectiveTrigger()`, `ActionConfig.Text`, `ActionConfig.Method`

- [ ] **Step 1: Write failing serialization + effective-trigger tests**

```csharp
// KeyMappingTriggerTests.cs
using Keysist.Config;
using Xunit;

public class KeyMappingTriggerTests
{
    [Fact]
    public void GetEffectiveTrigger_UsesLegacyKey_WhenTriggerMissing()
    {
        var m = new KeyMapping { Key = "NumPad1" };
        var t = m.GetEffectiveTrigger();
        Assert.Equal("key", t.Mode);
        Assert.Equal(new[] { "NumPad1" }, t.Keys);
    }

    [Fact]
    public void GetEffectiveTrigger_PrefersTrigger_OverLegacyKey()
    {
        var m = new KeyMapping
        {
            Key = "NumPad1",
            Trigger = new TriggerConfig { Mode = "chord", Keys = new List<string> { "LControlKey", "NumPad1" } }
        };
        var t = m.GetEffectiveTrigger();
        Assert.Equal("chord", t.Mode);
        Assert.Equal(new[] { "LControlKey", "NumPad1" }, t.Keys);
    }
}
```

Add to `ConfigModelsSerializationTests.cs`:

```csharp
[Fact]
public void RoundTrips_TriggerAndPasteText()
{
    var json = """
    {
      "devices": [{
        "name": "N",
        "devicePathMatch": "HID#VID_1&PID_2",
        "mappings": [{
          "trigger": { "mode": "sequence", "keys": ["NumPad7", "NumPad8"] },
          "action": { "type": "pasteText", "text": "hi", "method": "clipboard" }
        }]
      }]
    }
    """;
    var config = JsonSerializer.Deserialize<KeysistConfig>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    var m = config.Devices[0].Mappings![0];
    Assert.Equal("sequence", m.Trigger!.Mode);
    Assert.Equal(new[] { "NumPad7", "NumPad8" }, m.Trigger.Keys);
    Assert.Equal("pasteText", m.Action.Type);
    Assert.Equal("hi", m.Action.Text);
    Assert.Equal("clipboard", m.Action.Method);
}
```

- [ ] **Step 2: Run tests — expect FAIL**

Run: `dotnet test tests/Keysist.Tests/Keysist.Tests.csproj --filter "FullyQualifiedName~KeyMappingTriggerTests|FullyQualifiedName~RoundTrips_TriggerAndPasteText" -v n`  
Expected: compile errors (types/members missing).

- [ ] **Step 3: Implement models**

In `ConfigModels.cs` add:

```csharp
public class TriggerConfig
{
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "key"; // key | chord | sequence

    [JsonPropertyName("keys")]
    public List<string> Keys { get; set; } = new();
}

// On KeyMapping:
[JsonPropertyName("trigger")]
public TriggerConfig? Trigger { get; set; }

public TriggerConfig GetEffectiveTrigger()
{
    if (Trigger is not null && Trigger.Keys.Count > 0)
        return Trigger;
    return new TriggerConfig { Mode = "key", Keys = string.IsNullOrEmpty(Key) ? new() : new() { Key } };
}

// On ActionConfig:
[JsonPropertyName("text")]
public string? Text { get; set; }

[JsonPropertyName("method")]
public string? Method { get; set; } // type | clipboard for pasteText
```

- [ ] **Step 4: Run tests — expect PASS**

Run: `dotnet test tests/Keysist.Tests/Keysist.Tests.csproj --filter "FullyQualifiedName~KeyMappingTriggerTests|FullyQualifiedName~ConfigModelsSerializationTests" -v n`  
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/Keysist/Config/ConfigModels.cs tests/Keysist.Tests/KeyMappingTriggerTests.cs tests/Keysist.Tests/ConfigModelsSerializationTests.cs
git commit -m "feat: add trigger and pasteText config models"
```

---

### Task 2: VirtualKeyNames resolves Keys enum names

**Files:**
- Modify: `src/Keysist/Actions/VirtualKeyNames.cs`
- Create: `tests/Keysist.Tests/VirtualKeyNamesTests.cs`

**Interfaces:**
- Produces: `VirtualKeyNames.Resolve(string)` accepts `LControlKey`, `C`, `NumPad1`, media aliases, and `0xHH`

- [ ] **Step 1: Write failing tests**

```csharp
using Keysist.Actions;
using Xunit;

public class VirtualKeyNamesTests
{
    [Theory]
    [InlineData("LControlKey", 0xA2)]
    [InlineData("C", 0x43)]
    [InlineData("NumPad1", 0x61)]
    [InlineData("VK_MEDIA_PLAY_PAUSE", 0xB3)]
    [InlineData("0x1B", 0x1B)]
    public void Resolve_KnownNames(string name, int expected) =>
        Assert.Equal((ushort)expected, VirtualKeyNames.Resolve(name));
}
```

- [ ] **Step 2: Run — expect FAIL** on `LControlKey` / `C` / `NumPad1`

Run: `dotnet test tests/Keysist.Tests/Keysist.Tests.csproj --filter "FullyQualifiedName~VirtualKeyNamesTests" -v n`

- [ ] **Step 3: Implement fallback**

After dictionary/hex checks in `Resolve`:

```csharp
if (Enum.TryParse<System.Windows.Forms.Keys>(name, ignoreCase: true, out var keys)
    && keys != System.Windows.Forms.Keys.None)
{
    return (ushort)keys;
}
throw new ArgumentException($"Unknown virtual key name '{name}'");
```

- [ ] **Step 4: Run — expect PASS**

- [ ] **Step 5: Commit**

```bash
git add src/Keysist/Actions/VirtualKeyNames.cs tests/Keysist.Tests/VirtualKeyNamesTests.cs
git commit -m "fix: resolve WinForms Keys names in VirtualKeyNames"
```

---

### Task 3: CaptureSession (Start/Stop, modes, device filter)

**Files:**
- Create: `src/Keysist/Capture/CaptureSession.cs`
- Create: `tests/Keysist.Tests/CaptureSessionTests.cs`

**Interfaces:**
- Produces:

```csharp
namespace Keysist.Capture;

public enum CaptureMode { Key, Chord, Sequence }

public sealed class CaptureResult
{
    public required string DevicePath { get; init; }
    public required CaptureMode Mode { get; init; }
    public required IReadOnlyList<string> Keys { get; init; }
}

public sealed class CaptureSession
{
    public bool IsCapturing { get; }
    public CaptureResult? Result { get; }
    public void Start(CaptureMode mode, string? devicePathFilter = null);
    public void Stop();
    public void OnRawKeyDown(int vkey, string devicePath);
    public void OnRawKeyUp(int vkey, string devicePath);
}
```

Key naming: `((System.Windows.Forms.Keys)vkey).ToString()` — same as pipeline.

Behavior:
- Before Start / after Stop: ignore events (Stop freezes Result).
- Key mode: first accepted key-down sets Result and auto-stops capturing (IsCapturing=false, Result set).
- Chord mode: track downs on filtered device; when ≥1 key down and a new down arrives, Result.keys = currently held set including new key (modifiers+key); auto-stop when a non-modifier key arrives while any modifier is held, OR require Stop. Spec simplification for tests: **auto-stop when any key-down occurs while at least one other key is already held**; Result.Keys = sorted held keys at that moment (use stable order: order of press). If Stop pressed with held keys, use held set.
- Sequence mode: append each key-down until Stop; Result requires ≥1 key (wizard validates ≥2).
- Device filter: ignore other devices when filter set; first key on unfiltered capture pins the device for the rest of the session.

- [ ] **Step 1: Write failing tests** covering: no overwrite after stop; key auto-stop; device filter; sequence append until stop; chord with two held keys.

- [ ] **Step 2: Run — FAIL (type missing)**

- [ ] **Step 3: Implement `CaptureSession`**

- [ ] **Step 4: Run — PASS**

- [ ] **Step 5: Commit**

```bash
git add src/Keysist/Capture/CaptureSession.cs tests/Keysist.Tests/CaptureSessionTests.cs
git commit -m "feat: add CaptureSession with start/stop modes"
```

---

### Task 4: TriggerMatcher (key / chord / sequence + legacy)

**Files:**
- Create: `src/Keysist/Config/TriggerMatcher.cs`
- Create: `tests/Keysist.Tests/TriggerMatcherTests.cs`

**Interfaces:**
- Produces:

```csharp
namespace Keysist.Config;

public sealed class TriggerMatcher
{
    public TriggerMatcher(long sequenceGapMs = 1000);
    public void OnKeyDown(string devicePath, string keyName);
    public void OnKeyUp(string devicePath, string keyName);
    // Returns the matched mapping index or null; evaluates mappings in order.
    public KeyMapping? Match(DeviceConfig device, string devicePath, string keyNameJustPressed);
}
```

Rules:
- Use `mapping.GetEffectiveTrigger()`.
- key: match when `keys[0]` equals keyNameJustPressed (ignore case).
- chord: all keys in trigger are currently down on devicePath after applying this key-down; fire on the press that completes the set.
- sequence: maintain per-device list of recent key names with timestamps; on each down, drop entries older than `sequenceGapMs` from previous; if buffer ends with trigger.keys (ignore case), match and clear buffer.
- `anyKeyAction`: if set, Match returns a synthetic mapping or the caller keeps existing any-key path — **keep any-key handling in InputPipeline**, TriggerMatcher only for `Mappings`.

- [ ] **Step 1: Write failing tests** for key, chord, sequence, and legacy Key field.

- [ ] **Step 2: Run — FAIL**

- [ ] **Step 3: Implement TriggerMatcher**

- [ ] **Step 4: Run — PASS**

- [ ] **Step 5: Commit**

```bash
git add src/Keysist/Config/TriggerMatcher.cs tests/Keysist.Tests/TriggerMatcherTests.cs
git commit -m "feat: add TriggerMatcher for key/chord/sequence"
```

---

### Task 5: RawKeyUp + pipeline wiring + capture suppress

**Files:**
- Modify: `src/Keysist/RawInputInterop/RawInputWindow.cs`
- Modify: `src/Keysist/Pipeline/InputPipeline.cs`

**Interfaces:**
- Produces: `RawInputWindow.RawKeyUp`, `InputPipeline.RawKeyUp`, `InputPipeline.IsCaptureActive` (bool property settable by UI), dispatch skipped when true; TriggerMatcher used for mapping resolve/swallow.

Behavior changes in `OnRawKeyDown`:
1. Always update TriggerMatcher key state.
2. If `IsCaptureActive`, return (no dispatch).
3. Else resolve via anyKeyAction OR TriggerMatcher.Match.
4. Swallow path uses same resolve.

On raw key-up: forward event + `TriggerMatcher.OnKeyUp`.

For key-up in RawInputWindow: when `(raw.Keyboard.Flags & 1) != 0`, invoke `RawKeyUp` instead of returning silently.

- [ ] **Step 1: No automated hook test (native). Add a small unit-testable seam if needed — prefer extracting resolve into a method tested via TriggerMatcher (already done). Manually ensure build.**

- [ ] **Step 2: Implement RawKeyUp + pipeline changes**

- [ ] **Step 3: `dotnet build` + `dotnet test` — all pass; existing correlator tests unchanged.

- [ ] **Step 4: Commit**

```bash
git add src/Keysist/RawInputInterop/RawInputWindow.cs src/Keysist/Pipeline/InputPipeline.cs
git commit -m "feat: raw key-up, trigger matching, capture suppress"
```

---

### Task 6: sendKeys keydown/keyup + pasteText dispatcher

**Files:**
- Modify: `src/Keysist/Actions/IKeySender.cs` (if needed — keep Send(steps))
- Modify: `src/Keysist/Actions/SendInputKeySender.cs`
- Create: `src/Keysist/Actions/IClipboard.cs`
- Create: `src/Keysist/Actions/WindowsClipboard.cs`
- Modify: `src/Keysist/Actions/ActionDispatcher.cs`
- Modify: `tests/Keysist.Tests/ActionDispatcherTests.cs`
- Create: `tests/Keysist.Tests/FakeClipboard.cs`
- Create: `tests/Keysist.Tests/SendStepExpandTests.cs` (optional helper tests)

**Interfaces:**
- Produces:

```csharp
public interface IClipboard
{
    void SetText(string text);
}

public ActionDispatcher(IProcessLauncher launcher, IKeySender sender, IClipboard clipboard);
// Dispatch pasteText:
//  method type -> sender.Send([text step])
//  method clipboard -> clipboard.SetText + send Ctrl+V via keydown/vkey/keyup steps
```

SendInputKeySender: handle `keydown` / `keyup` (single INPUT, no pair). Keep `vkey` as down+up.

- [ ] **Step 1: Write failing ActionDispatcher tests for pasteText type and clipboard**

- [ ] **Step 2: Run — FAIL**

- [ ] **Step 3: Implement clipboard + dispatcher + sender branches**

- [ ] **Step 4: Run full test suite — PASS**

- [ ] **Step 5: Commit**

```bash
git add src/Keysist/Actions tests/Keysist.Tests
git commit -m "feat: keydown/keyup sendKeys and pasteText actions"
```

---

### Task 7: CaptureControl + RegisterDeviceWindow

**Files:**
- Create: `src/Keysist/Ui/CaptureControl.cs`
- Create: `src/Keysist/Ui/RegisterDeviceWindow.cs`
- Modify: `src/Keysist/Tray/TrayIconController.cs` (wire Register Device; can wait until Task 9 for full menu — this task only adds window + control)
- Delete after Task 9: DiscoverDeviceWindow

CaptureControl: UserControl with mode ComboBox (Key/Chord/Sequence), Start/Stop buttons, result Label. Takes `InputPipeline` + `CaptureSession`. On Start: set `pipeline.IsCaptureActive=true`, subscribe RawKeyDown/Up → session. On Stop or auto-stop: unsubscribe, `IsCaptureActive=false`. Expose `CaptureResult? Result`.

RegisterDeviceWindow: CaptureControl in Key mode → name TextBox → Save creates/updates DeviceConfig with DevicePathMatch via existing ShortPath logic from DiscoverDeviceWindow (extract static helper to `DeviceIdentity.ShortMatch` or keep private copy).

- [ ] **Step 1: Implement CaptureControl + RegisterDeviceWindow**

- [ ] **Step 2: `dotnet build` — PASS**

- [ ] **Step 3: Commit**

```bash
git add src/Keysist/Ui/CaptureControl.cs src/Keysist/Ui/RegisterDeviceWindow.cs
git commit -m "feat: capture control and register-device window"
```

---

### Task 8: NewMappingWizard

**Files:**
- Create: `src/Keysist/Ui/NewMappingWizard.cs`
- Create: `src/Keysist/Ui/ActionPickerPanel.cs` (optional inline in wizard if smaller)

Wizard pages (Panel swap or TabControl without tabs visible):
1. Trigger — CaptureControl; optional device ComboBox filter from config; on next, require Result; if device unknown prompt name and add DeviceConfig.
2. Action — radios Send keys / Paste text / Launch. Send keys: second CaptureControl (any device default). Paste: TextBox + method Combo. Launch: path/args/workdir.
3. Options — swallow checkbox default true → Save writes KeyMapping with Trigger + Action.

Validation: Launch requires path; Send keys requires capture result (≥1 key); Paste requires non-empty text; Sequence trigger should warn if <2 keys.

Map CaptureMode → trigger.mode strings `key|chord|sequence`.  
Map action capture keys → sendKeys steps: if 1 key → one vkey step; if multiple → keydown all but last, vkey last, keyup others reversed (chord replay). For sequence mode action capture, emit vkey steps in order with delayMs=0.

- [ ] **Step 1: Implement wizard**

- [ ] **Step 2: `dotnet build` — PASS**

- [ ] **Step 3: Commit**

```bash
git add src/Keysist/Ui/NewMappingWizard.cs src/Keysist/Ui/ActionPickerPanel.cs
git commit -m "feat: new mapping wizard with capture-first flow"
```

---

### Task 9: MainMappingsWindow + tray menu; retire old UI

**Files:**
- Create: `src/Keysist/Ui/MainMappingsWindow.cs`
- Modify: `src/Keysist/Tray/TrayIconController.cs`
- Delete: `src/Keysist/Ui/DiscoverDeviceWindow.cs`
- Delete: `src/Keysist/Ui/EditMappingsWindow.cs` (functionality replaced; keep ActionEditorDialog for edit if useful)
- Adapt: `src/Keysist/Ui/ActionEditorDialog.cs` — support pasteText + trigger display; or edit opens NewMappingWizard prefilled — **prefer opening a simplified Edit dialog that reuses ActionPickerPanel and swallow**

Tray menu:
- Devices & Mappings → MainMappingsWindow
- New Mapping… → NewMappingWizard
- Register Device… → RegisterDeviceWindow
- Reload Config / Exit

Main window: ListBox devices, ListView mappings, buttons New/Edit/Delete/Register, swallow checkbox for selected mapping.

- [ ] **Step 1: Implement main window + tray; delete old windows; fix all references**

- [ ] **Step 2: `dotnet build` + `dotnet test` — PASS**

- [ ] **Step 3: Commit**

```bash
git add -A src/Keysist tests/Keysist.Tests
git commit -m "feat: main mappings UI; remove discover/edit legacy windows"
```

---

### Task 10: Publish script + gitignore dist

**Files:**
- Create: `publish.ps1`
- Modify: `.gitignore`

- [ ] **Step 1: Update `.gitignore`**

```
bin/
obj/
*.user
dist/
.worktrees/
```

- [ ] **Step 2: Create `publish.ps1`**

```powershell
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
dotnet publish "$root\src\Keysist\Keysist.csproj" -c Release -r win-x64 --self-contained false -o "$root\dist"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "Published to $root\dist"
```

- [ ] **Step 3: Run publish**

```powershell
powershell -ExecutionPolicy Bypass -File .\publish.ps1
```

Expected: `dist\Keysist.exe` exists.

- [ ] **Step 4: Commit script + gitignore only (not dist)**

```bash
git add .gitignore publish.ps1
git commit -m "chore: publish Release builds to dist/"
```

---

### Task 11: Full verification

- [ ] **Step 1: `dotnet test` — all pass**

- [ ] **Step 2: `.\publish.ps1` — dist exe present**

- [ ] **Step 3: Smoke-run `dist\Keysist.exe` briefly (start, confirm process, stop). Do not leave autostart pointing at a worktree build if tests toggle it — clean up Run key if dirty.

- [ ] **Step 4: Commit any doc touch-ups if needed; otherwise no commit**

---

## Spec coverage check

| Spec requirement | Task |
|------------------|------|
| Start/Stop capture | 3, 7 |
| Chord + sequence triggers | 3, 4, 5 |
| Auto-register device from capture | 8 |
| Pre-register device | 7 |
| Send keys capture / chord replay | 6, 8 |
| Paste text type \| clipboard | 6, 8 |
| Launch only when chosen | 8 |
| No second RawInputWindow | 5, 7 |
| Capture suppresses dispatch | 5 |
| Legacy key mappings | 1, 4 |
| Tray + main window | 9 |
| Publish to dist/ | 10, 11 |
| VirtualKeyNames for C/Ctrl | 2 |

## Execution

User requested **subagent-driven development** after this plan is saved.
