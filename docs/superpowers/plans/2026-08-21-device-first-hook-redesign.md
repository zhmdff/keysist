# Device-First Hook Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate the system-wide keyboard freeze caused by `InputPipeline.OnShouldEatVkey` blocking the global `WH_KEYBOARD_LL` callback thread for up to 200ms while it waits for a cross-thread Raw Input correlation, by identifying the source device *synchronously and immediately* inside the hook callback instead of guessing "is this key mapped on any device" first and waiting to find out which device sent it.

**Architecture:** Replace the async `RawInputPump` thread + `InputCorrelator` (timing-based, cross-thread, best-effort) with a synchronous `RawInputBufferReader` that calls `GetRawInputBuffer` (the batch/synchronous Win32 raw-input API) directly from the LL hook callback thread. `GetRawInputBuffer` drains the OS-maintained raw-input queue in-process on whatever thread calls it — no message pump, no other thread, no wait required. This means: the moment `WH_KEYBOARD_LL` fires, we can, in the same call, synchronously ask "which device just sent this vkey" and get an immediate answer (device path known before any swallow/dispatch decision is made) — device is resolved *first*, then matched against that device's mappings only. `SwallowPlanner`'s cross-device "is this key a candidate anywhere" heuristic — the thing that made pressing a mapped key on an *unmapped* device stall — is deleted; it's no longer needed because we never have to guess across all devices while waiting to learn which device it was.

Also fixes: dispatch continuing to route through `_uiContext.Post` unchanged (out of scope — that part is fine), but resolves the "press button while some other window focused, nothing happens until refocus" symptom as a side effect, since it was caused by the same hook-thread stall backing up the global input queue.

**Tech Stack:** C#/.NET, WinUI 3, Win32 P/Invoke (`user32.dll`), xUnit for the new `Keysist.Core.Tests` project (no existing test project — created in Task 1).

**Spec:** No separate spec doc — the Architecture section above *is* the spec, derived from live root-cause investigation (see conversation: `InputPipeline.OnShouldEatVkey` at `src/Keysist.Core/Pipeline/InputPipeline.cs:181`, busy-waits up to `CorrelateWaitMs=200`ms on `_rawArrived` while `SwallowPlanner.IsTentativeSwallowCandidate` at `src/Keysist.Core/Config/SwallowPlanner.cs:10` triggers that wait for ANY device's mapping, not just the one that actually fired).

## Global Constraints

- Never block inside `LowLevelKeyboardHook.HookCallback` (`src/Keysist.Core/Hooking/LowLevelKeyboardHook.cs:60`) — no `Wait`, `Sleep`, or blocking I/O. Windows silently unhooks LL hooks that are slow (`LowLevelHooksTimeout`, default ~300ms) and every ms here is a system-wide keyboard stall.
- `GetRawInputBuffer` on 64-bit processes requires 8-byte-aligned advancement between `RAWINPUT` records in the buffer — the documented pitfall (raw `header.dwSize` is NOT the stride; you must round up to `sizeof(nint)`). Get this wrong and buffer parsing walks off into garbage.
- Device resolution must happen **before** any mapping/swallow decision — no code path may consult `_configStore.Current.Devices` without already knowing the concrete device.
- Keep `IKeySender`/`ActionDispatcher`/dispatch-to-UI-thread logic (`InputPipeline.cs:135-152`) unchanged — out of scope.
- All new logic that doesn't require live hardware raw input must be unit-testable (buffer parsing, device/vkey matching, pipeline decision logic via an injected fake resolver).

---

## File Structure

- Create: `src/Keysist.Core/RawInputInterop/RawInputBufferReader.cs` — owns registration + synchronous `GetRawInputBuffer` draining; replaces `RawInputPump`'s role of "tell me the device for this event" but keeps a small in-memory ring of recent parsed events.
- Create: `src/Keysist.Core/RawInputInterop/RawInputBufferParser.cs` — pure function: raw byte buffer → list of parsed `(vkey, isKeyUp, devicePath, timestampMs)` records. Pure/testable, no P/Invoke calls beyond struct marshaling of an already-obtained byte buffer.
- Modify: `src/Keysist.Core/RawInputInterop/RawInputNative.cs` — add `GetRawInputBuffer` P/Invoke + `NEXTRAWINPUTBLOCK`-equivalent alignment helper.
- Modify: `src/Keysist.Core/Pipeline/InputPipeline.cs` — remove `InputCorrelator`, `_rawArrived`, `CorrelateWaitMs`, `RawInputPump` usage; `OnShouldEatVkey`/`OnShouldEatVkeyUp` resolve device synchronously via `RawInputBufferReader` and match only that device's mappings. `OnRawKeyDown`/`OnRawKeyUp` become internal handlers invoked directly by the hook (no more separate `RawInputPump` thread wiring for the dispatch path either — see Task 4).
- Modify: `src/Keysist.Core/Hooking/LowLevelKeyboardHook.cs` — hook callback now resolves device+dispatch inline (calls back into pipeline synchronously for the *decision*, still posts the actual action dispatch to the UI thread as before).
- Delete (after migration): `src/Keysist.Core/Correlation/InputCorrelator.cs`, `src/Keysist.Core/RawInputInterop/RawInputPump.cs`, `src/Keysist.Core/RawInputInterop/RawInputWindow.cs`'s WM_INPUT dispatch path (keep the hidden window only for `RegisterRawInputDevices` target requirement — see Task 4 for exact scope).
- Modify: `src/Keysist.Core/Config/SwallowPlanner.cs` — delete `IsTentativeSwallowCandidate` (no longer called); keep `IsModifierKey` (still used to skip modifier keys from swallow logic).
- Create: `src/Keysist.Core.Tests/Keysist.Core.Tests.csproj` — new xUnit test project referencing `Keysist.Core`.
- Create: `src/Keysist.Core.Tests/RawInputBufferParserTests.cs`.
- Create: `src/Keysist.Core.Tests/InputPipelineDeviceResolutionTests.cs`.
- Modify: `src/Keysist.Core/Diagnostics/Logger.cs` (read first — see Task 6) — add a hook-latency log line so future stalls are visible immediately instead of requiring re-investigation.

---

## Task 1: Create test project scaffold

**Files:**
- Create: `src/Keysist.Core.Tests/Keysist.Core.Tests.csproj`
- Create: `src/Keysist.Core.Tests/PlaceholderTests.cs`

**Interfaces:**
- Produces: a buildable/runnable `dotnet test` target other tasks add tests into.

- [ ] **Step 1: Check target framework of Keysist.Core**

Run: `cat src/Keysist.Core/Keysist.Core.csproj`
Note the `<TargetFramework>` value (expected `net8.0-windows` or similar — copy exactly).

- [ ] **Step 2: Create the test csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Keysist.Core\Keysist.Core.csproj" />
  </ItemGroup>

</Project>
```

(Replace `<TargetFramework>` with the exact value found in Step 1 if different.)

- [ ] **Step 3: Add a placeholder test**

```csharp
namespace Keysist.Core.Tests;

public class PlaceholderTests
{
    [Fact]
    public void Sanity_check_project_builds_and_runs()
    {
        Assert.True(true);
    }
}
```

- [ ] **Step 4: Run it**

Run: `dotnet test src/Keysist.Core.Tests/Keysist.Core.Tests.csproj`
Expected: 1 passed.

- [ ] **Step 5: Commit**

```bash
git add src/Keysist.Core.Tests
git commit -m "test: add Keysist.Core.Tests project scaffold"
```

---

## Task 2: RawInputBufferParser — pure buffer parsing

**Files:**
- Create: `src/Keysist.Core/RawInputInterop/RawInputBufferParser.cs`
- Modify: `src/Keysist.Core/RawInputInterop/RawInputNative.cs`
- Test: `src/Keysist.Core.Tests/RawInputBufferParserTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  namespace Keysist.RawInputInterop;

  public readonly record struct RawKeyEvent(int Vkey, bool IsKeyUp, IntPtr DeviceHandle, long TimestampMs);

  public static class RawInputBufferParser
  {
      // Parses a raw byte buffer filled by GetRawInputBuffer into a list of keyboard events.
      // rawinputSize is the RAWINPUT struct's marshaled size as returned by
      // Marshal.SizeOf<RawInputNative.RAWINPUT>() — passed in so the parser has no
      // P/Invoke dependency and is independently testable.
      public static List<RawKeyEvent> Parse(byte[] buffer, int recordCount, int rawinputSize, long nowMs);

      // The documented GetRawInputBuffer pitfall: on 64-bit, consecutive RAWINPUT
      // records are NOT packed at header.dwSize apart — they're aligned up to
      // sizeof(IntPtr). This computes the real stride.
      public static int AlignedStride(int rawinputSize);
  }
  ```

**Note:** `RawKeyEvent` carries `DeviceHandle` (the raw `HANDLE`, not yet resolved to a device path string) — path resolution via `GetRawInputDeviceInfo` (existing `DeviceIdentity.ResolveDevicePath`) happens one layer up in `RawInputBufferReader` (Task 3), since it requires a live P/Invoke call per handle and isn't pure/testable. Keeping it out of the parser keeps the parser a pure function testable with byte arrays.

- [ ] **Step 1: Write the failing test — alignment stride**

```csharp
using Keysist.RawInputInterop;

namespace Keysist.Core.Tests;

public class RawInputBufferParserTests
{
    [Theory]
    [InlineData(40, 40)]   // already 8-aligned
    [InlineData(37, 40)]   // rounds up to next multiple of 8
    [InlineData(41, 48)]
    public void AlignedStride_rounds_up_to_pointer_size(int rawSize, int expectedStride)
    {
        Assert.Equal(expectedStride, RawInputBufferParser.AlignedStride(rawSize));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test src/Keysist.Core.Tests/Keysist.Core.Tests.csproj --filter AlignedStride_rounds_up_to_pointer_size`
Expected: FAIL — `RawInputBufferParser` does not exist.

- [ ] **Step 3: Add `GetRawInputBuffer` P/Invoke to RawInputNative.cs**

Add inside `internal static class RawInputNative` (append, don't touch existing members):

```csharp
    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetRawInputBuffer(IntPtr pData, ref uint pcbSize, uint cbSizeHeader);
```

- [ ] **Step 4: Implement `RawInputBufferParser.AlignedStride` and `Parse`**

```csharp
// src/Keysist.Core/RawInputInterop/RawInputBufferParser.cs
using System.Runtime.InteropServices;

namespace Keysist.RawInputInterop;

public readonly record struct RawKeyEvent(int Vkey, bool IsKeyUp, IntPtr DeviceHandle, long TimestampMs);

public static class RawInputBufferParser
{
    public static int AlignedStride(int rawinputSize)
    {
        var ptrSize = IntPtr.Size;
        return (rawinputSize + ptrSize - 1) / ptrSize * ptrSize;
    }

    public static List<RawKeyEvent> Parse(byte[] buffer, int recordCount, int rawinputSize, long nowMs)
    {
        var results = new List<RawKeyEvent>(recordCount);
        var stride = AlignedStride(rawinputSize);
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            var basePtr = handle.AddrOfPinnedObject();
            for (var i = 0; i < recordCount; i++)
            {
                var recordPtr = IntPtr.Add(basePtr, i * stride);
                var raw = Marshal.PtrToStructure<RawInputNative.RAWINPUT>(recordPtr);
                if (raw.Header.Type != RawInputNative.RIM_TYPEKEYBOARD)
                    continue;

                var isKeyUp = (raw.Keyboard.Flags & 1) != 0;
                results.Add(new RawKeyEvent(raw.Keyboard.VKey, isKeyUp, raw.Header.Device, nowMs));
            }
        }
        finally
        {
            handle.Free();
        }
        return results;
    }
}
```

- [ ] **Step 5: Run alignment test**

Run: `dotnet test src/Keysist.Core.Tests/Keysist.Core.Tests.csproj --filter AlignedStride_rounds_up_to_pointer_size`
Expected: PASS (3 cases).

- [ ] **Step 6: Write the failing test — Parse extracts keyboard records and skips non-keyboard**

```csharp
    [Fact]
    public void Parse_extracts_keyboard_records_and_skips_others()
    {
        var rawinputSize = Marshal.SizeOf<RawInputNative.RAWINPUT>();
        var stride = RawInputBufferParser.AlignedStride(rawinputSize);
        var buffer = new byte[stride * 2];

        WriteKeyboardRecord(buffer, offset: 0, vkey: 65, isKeyUp: false, device: new IntPtr(0x1111));
        WriteNonKeyboardRecord(buffer, offset: stride);

        var events = RawInputBufferParser.Parse(buffer, recordCount: 2, rawinputSize, nowMs: 1000);

        var e = Assert.Single(events);
        Assert.Equal(65, e.Vkey);
        Assert.False(e.IsKeyUp);
        Assert.Equal(new IntPtr(0x1111), e.DeviceHandle);
        Assert.Equal(1000, e.TimestampMs);
    }

    private static void WriteKeyboardRecord(byte[] buffer, int offset, int vkey, bool isKeyUp, IntPtr device)
    {
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            var ptr = IntPtr.Add(handle.AddrOfPinnedObject(), offset);
            var raw = new RawInputNative.RAWINPUT
            {
                Header = new RawInputNative.RAWINPUTHEADER
                {
                    Type = RawInputNative.RIM_TYPEKEYBOARD,
                    Device = device,
                },
                Keyboard = new RawInputNative.RAWKEYBOARD
                {
                    VKey = (ushort)vkey,
                    Flags = (ushort)(isKeyUp ? 1 : 0),
                },
            };
            Marshal.StructureToPtr(raw, ptr, false);
        }
        finally
        {
            handle.Free();
        }
    }

    private static void WriteNonKeyboardRecord(byte[] buffer, int offset)
    {
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            var ptr = IntPtr.Add(handle.AddrOfPinnedObject(), offset);
            var raw = new RawInputNative.RAWINPUT
            {
                Header = new RawInputNative.RAWINPUTHEADER { Type = 2 /* RIM_TYPEMOUSE */ },
            };
            Marshal.StructureToPtr(raw, ptr, false);
        }
        finally
        {
            handle.Free();
        }
    }
```

- [ ] **Step 7: Run to verify it fails first, then implement, then pass**

Run: `dotnet test src/Keysist.Core.Tests/Keysist.Core.Tests.csproj --filter Parse_extracts_keyboard_records_and_skips_others`
(It should already pass given Step 4's implementation — if it fails, fix `Parse` until it does. Do not proceed until green.)

- [ ] **Step 8: Commit**

```bash
git add src/Keysist.Core/RawInputInterop/RawInputBufferParser.cs src/Keysist.Core/RawInputInterop/RawInputNative.cs src/Keysist.Core.Tests/RawInputBufferParserTests.cs
git commit -m "feat: add synchronous raw input buffer parser"
```

---

## Task 3: RawInputBufferReader — synchronous device resolution, no waiting

**Files:**
- Create: `src/Keysist.Core/RawInputInterop/RawInputBufferReader.cs`
- Modify: `src/Keysist.Core/RawInputInterop/RawInputNative.cs`
- Test: `src/Keysist.Core.Tests/RawInputBufferReaderTests.cs`

**Interfaces:**
- Consumes: `RawInputBufferParser.Parse` (Task 2), `DeviceIdentity.ResolveDevicePath(IntPtr)` (existing, `src/Keysist.Core/RawInputInterop/DeviceIdentity.cs:19`).
- Produces:
  ```csharp
  namespace Keysist.RawInputInterop;

  public interface IRawEventResolver
  {
      // Drains whatever is currently queued and returns the device path for the
      // most recent matching (vkey, isKeyUp) event, or null if none is queued.
      // MUST NOT block or sleep.
      string? TryResolveDevicePath(int vkey, bool isKeyUp);
  }

  public class RawInputBufferReader : IRawEventResolver
  {
      public void Register(IntPtr targetWindowHandle);
      public string? TryResolveDevicePath(int vkey, bool isKeyUp);
  }
  ```
  `IRawEventResolver` is the seam `InputPipeline` (Task 5) depends on, so pipeline tests can inject a fake instead of touching real hardware.

- [ ] **Step 1: Write the failing test — fake queue resolves most recent match**

```csharp
// src/Keysist.Core.Tests/RawInputBufferReaderTests.cs
using Keysist.RawInputInterop;

namespace Keysist.Core.Tests;

public class RawInputBufferReaderTests
{
    [Fact]
    public void TryResolveDevicePath_returns_null_when_no_events_queued()
    {
        var reader = new RawInputBufferReader();
        Assert.Null(reader.TryResolveDevicePath(vkey: 65, isKeyUp: false));
    }
}
```

This first test only proves the "nothing queued → null, never blocks" contract, which is
achievable without mocking `GetRawInputBuffer` (an unregistered/never-drained reader has
nothing pending). Real drain behavior against live hardware cannot be unit tested — that's
inherent to raw input requiring a real device; it's covered by Task 6's manual verification
steps instead.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test src/Keysist.Core.Tests/Keysist.Core.Tests.csproj --filter TryResolveDevicePath_returns_null_when_no_events_queued`
Expected: FAIL — `RawInputBufferReader` does not exist.

- [ ] **Step 3: Add `RIDEV_INPUTSINK`-registration-independent buffer-size query constant**

Confirm `RawInputNative.cs` already has `RIDEV_INPUTSINK`, `HID_USAGE_PAGE_GENERIC`, `HID_USAGE_GENERIC_KEYBOARD`, `RAWINPUTDEVICE`, `RegisterRawInputDevices` (it does — no change needed there).

- [ ] **Step 4: Implement `RawInputBufferReader`**

```csharp
// src/Keysist.Core/RawInputInterop/RawInputBufferReader.cs
using System.Runtime.InteropServices;
using Keysist.Diagnostics;

namespace Keysist.RawInputInterop;

public interface IRawEventResolver
{
    string? TryResolveDevicePath(int vkey, bool isKeyUp);
}

/// <summary>
/// Resolves which HID device sent a keystroke by draining the OS raw-input queue
/// synchronously via GetRawInputBuffer — callable from the LL keyboard hook thread
/// with no waiting, no other thread, no message pump involved.
/// </summary>
public class RawInputBufferReader : IRawEventResolver
{
    private readonly int _rawinputSize = Marshal.SizeOf<RawInputNative.RAWINPUT>();
    private readonly List<RawKeyEvent> _recent = new();
    private const long RecentTtlMs = 250;

    public void Register(IntPtr targetWindowHandle)
    {
        var devices = new[]
        {
            new RawInputNative.RAWINPUTDEVICE
            {
                UsagePage = RawInputNative.HID_USAGE_PAGE_GENERIC,
                Usage = RawInputNative.HID_USAGE_GENERIC_KEYBOARD,
                Flags = RawInputNative.RIDEV_INPUTSINK,
                Target = targetWindowHandle,
            },
        };
        var registered = RawInputNative.RegisterRawInputDevices(
            devices, (uint)devices.Length, (uint)Marshal.SizeOf<RawInputNative.RAWINPUTDEVICE>());
        Logger.Log(registered
            ? "RawInputBufferReader.Register: RegisterRawInputDevices succeeded."
            : "RawInputBufferReader.Register: RegisterRawInputDevices FAILED -- raw input will not be received.");
    }

    public string? TryResolveDevicePath(int vkey, bool isKeyUp)
    {
        Drain();

        var now = Environment.TickCount64;
        for (var i = _recent.Count - 1; i >= 0; i--)
        {
            var evt = _recent[i];
            if (now - evt.TimestampMs > RecentTtlMs)
            {
                _recent.RemoveAt(i);
                continue;
            }
            if (evt.Vkey == vkey && evt.IsKeyUp == isKeyUp)
            {
                _recent.RemoveAt(i);
                return DeviceIdentity.ResolveDevicePath(evt.DeviceHandle);
            }
        }
        return null;
    }

    private void Drain()
    {
        uint size = 0;
        var headerSize = (uint)Marshal.SizeOf<RawInputNative.RAWINPUTHEADER>();
        var probe = RawInputNative.GetRawInputBuffer(IntPtr.Zero, ref size, headerSize);
        if (size == 0)
            return;

        // GetRawInputBuffer wants a buffer sized for several records; ask for a handful
        // up front rather than looping single records to keep this call count low.
        var stride = RawInputBufferParser.AlignedStride((int)size);
        var capacity = 16;
        var buffer = new byte[stride * capacity];
        uint bufSize = (uint)buffer.Length;

        var pinned = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            var count = RawInputNative.GetRawInputBuffer(pinned.AddrOfPinnedObject(), ref bufSize, headerSize);
            if (count == unchecked((uint)-1) || count == 0)
                return;

            var now = Environment.TickCount64;
            var parsed = RawInputBufferParser.Parse(buffer, (int)count, (int)size, now);
            _recent.AddRange(parsed);
        }
        finally
        {
            pinned.Free();
        }
    }
}
```

- [ ] **Step 5: Run test**

Run: `dotnet test src/Keysist.Core.Tests/Keysist.Core.Tests.csproj --filter TryResolveDevicePath_returns_null_when_no_events_queued`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Keysist.Core/RawInputInterop/RawInputBufferReader.cs src/Keysist.Core.Tests/RawInputBufferReaderTests.cs
git commit -m "feat: add synchronous raw input device resolver"
```

---

## Task 4: Collapse RawInputWindow/RawInputPump into registration-only, delete InputCorrelator

**Files:**
- Modify: `src/Keysist.Core/RawInputInterop/RawInputWindow.cs` — strip WM_INPUT handling, keep only as the hidden `HWND_MESSAGE` target `RawInputBufferReader.Register` needs.
- Delete: `src/Keysist.Core/RawInputInterop/RawInputPump.cs`
- Delete: `src/Keysist.Core/Correlation/InputCorrelator.cs`

**Interfaces:**
- Consumes: `RawInputBufferReader.Register(IntPtr)` (Task 3).
- Produces: `RawInputWindow` now exposes just `IntPtr Handle` + `Start()`/`Stop()` for window lifecycle — no more `RawKeyDown`/`RawKeyUp` events.

- [ ] **Step 1: Rewrite RawInputWindow.cs**

```csharp
// src/Keysist/RawInputInterop/RawInputWindow.cs
namespace Keysist.RawInputInterop;

/// <summary>
/// A hidden message-only window that exists solely to satisfy RegisterRawInputDevices'
/// target-window requirement. Actual raw input is drained synchronously elsewhere via
/// RawInputBufferReader.TryResolveDevicePath — this window's WndProc does not need to
/// (and does not) handle WM_INPUT.
/// </summary>
public class RawInputWindow : System.Windows.Forms.NativeWindow, IDisposable
{
    public void Start()
    {
        CreateHandle(new System.Windows.Forms.CreateParams
        {
            Caption = "KeysistRawInput",
            Parent = new IntPtr(-3) // HWND_MESSAGE
        });
    }

    public void Stop() => DestroyHandle();

    public void Dispose() => Stop();
}
```

- [ ] **Step 2: Delete RawInputPump.cs and InputCorrelator.cs**

```bash
git rm src/Keysist.Core/RawInputInterop/RawInputPump.cs src/Keysist.Core/Correlation/InputCorrelator.cs
```

- [ ] **Step 3: Build to confirm what breaks (expected — InputPipeline still references old types; fixed in Task 5)**

Run: `dotnet build src/Keysist.Core/Keysist.Core.csproj`
Expected: FAIL — `InputPipeline.cs` references `RawInputPump`, `InputCorrelator`. This is expected; Task 5 fixes it. Do not commit yet.

- [ ] **Step 4: Leave working tree as-is; proceed directly to Task 5 (same commit boundary)**

This task's deletions and Task 5's rewrite of `InputPipeline` land in a single commit at the end of Task 5, since the codebase doesn't build in between — don't create a broken intermediate commit.

---

## Task 5: Rewire InputPipeline + LowLevelKeyboardHook around synchronous device resolution

**Files:**
- Modify: `src/Keysist.Core/Pipeline/InputPipeline.cs`
- Modify: `src/Keysist.Core/Hooking/LowLevelKeyboardHook.cs`
- Modify: `src/Keysist.Core/Config/SwallowPlanner.cs`
- Test: `src/Keysist.Core.Tests/InputPipelineDeviceResolutionTests.cs`

**Interfaces:**
- Consumes: `IRawEventResolver.TryResolveDevicePath(int vkey, bool isKeyUp)` (Task 3), `RawInputWindow` (Task 4), `DeviceMatcher.FindMatch` (existing, unchanged), `TriggerMatcher` (existing, unchanged).
- Produces: `InputPipeline` constructor now takes an injectable `IRawEventResolver` (defaults to `RawInputBufferReader` in the parameterless path used by `Program.cs`) so tests can substitute a fake resolver and assert decisions never depend on cross-device state.

- [ ] **Step 1: Write the failing test — device isolation (the actual bug report)**

```csharp
// src/Keysist.Core.Tests/InputPipelineDeviceResolutionTests.cs
using Keysist.Config;
using Keysist.Pipeline;
using Keysist.RawInputInterop;

namespace Keysist.Core.Tests;

file class FakeResolver : IRawEventResolver
{
    public string? NextDevicePath;
    public int CallCount;

    public string? TryResolveDevicePath(int vkey, bool isKeyUp)
    {
        CallCount++;
        return NextDevicePath;
    }
}

public class InputPipelineDeviceResolutionTests
{
    private static ConfigStore BuildStoreWithMappedDeviceA()
    {
        var store = new ConfigStore();
        store.Current.Devices.Add(new DeviceConfig
        {
            Name = "NumPad",
            DevicePathMatch = "HID#VID_AAAA",
            DefaultSwallow = true,
            Mappings =
            [
                new MappingConfig
                {
                    Trigger = new TriggerConfig { Keys = ["A"] },
                    Action = new ActionConfig { Type = "launch", Path = "calc.exe" },
                    Swallow = true,
                }
            ],
        });
        return store;
    }

    [Fact]
    public void ShouldEatVkey_does_not_swallow_when_resolved_device_has_no_mapping()
    {
        var store = BuildStoreWithMappedDeviceA();
        var resolver = new FakeResolver { NextDevicePath = "HID#VID_BBBB&PID_0002" }; // unmapped device B
        var pipeline = new InputPipeline(store, resolver);

        var shouldEat = pipeline.TestOnlyShouldEatVkey(vkeyForKeyA: 65);

        Assert.False(shouldEat);
        Assert.Equal(1, resolver.CallCount); // resolved exactly once, synchronously — no retry loop
    }

    [Fact]
    public void ShouldEatVkey_swallows_when_resolved_device_has_matching_mapping()
    {
        var store = BuildStoreWithMappedDeviceA();
        var resolver = new FakeResolver { NextDevicePath = "HID#VID_AAAA&PID_0001" };
        var pipeline = new InputPipeline(store, resolver);

        var shouldEat = pipeline.TestOnlyShouldEatVkey(vkeyForKeyA: 65);

        Assert.True(shouldEat);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test src/Keysist.Core.Tests/Keysist.Core.Tests.csproj --filter InputPipelineDeviceResolutionTests`
Expected: FAIL — `InputPipeline` has no `(ConfigStore, IRawEventResolver)` constructor or `TestOnlyShouldEatVkey`.

- [ ] **Step 3: Rewrite InputPipeline.cs**

Replace the whole file:

```csharp
using Keysist.Actions;
using Keysist.Config;
using Keysist.Diagnostics;
using Keysist.Hooking;
using Keysist.RawInputInterop;

namespace Keysist.Pipeline;

public class InputPipeline : IDisposable
{
    private readonly ConfigStore _configStore;
    private readonly RawInputWindow _rawInputWindow = new();
    private readonly IRawEventResolver _resolver;
    private readonly LowLevelKeyboardHook _hook = new();
    private readonly IKeySender _keySender = new SendInputKeySender();
    private readonly ActionDispatcher _dispatcher;
    private readonly TriggerMatcher _triggerMatcher = new();
    private SynchronizationContext? _uiContext;

    private readonly Dictionary<(string DevicePath, int Vkey), long> _lastDispatchedAtMs = new();
    private readonly object _lastDispatchedLock = new();
    private const long DebounceMs = 300;

    private readonly object _hookAteLock = new();
    /// <summary>Vkeys whose KEYDOWN was eaten for the current hold (cleared on KEYUP).</summary>
    private readonly HashSet<int> _heldSwallowedVkeys = new();

    public bool IsCaptureActive { get; set; }

    public InputPipeline(ConfigStore configStore) : this(configStore, new RawInputBufferReader())
    {
    }

    public InputPipeline(ConfigStore configStore, IRawEventResolver resolver)
    {
        _configStore = configStore;
        _resolver = resolver;
        _dispatcher = new ActionDispatcher(new WindowsProcessLauncher(), _keySender, new WindowsClipboard());
    }

    public void Start()
    {
        _uiContext = SynchronizationContext.Current;
        Logger.Log($"Pipeline.Start: uiContext={(_uiContext is null ? "null" : "ok")} devices={_configStore.Current.Devices.Count}");

        _rawInputWindow.Start();
        if (_resolver is RawInputBufferReader realReader)
            realReader.Register(_rawInputWindow.Handle);

        _hook.ShouldEatVkey = OnShouldEatVkey;
        _hook.ShouldEatVkeyUp = OnShouldEatVkeyUp;
        _hook.Install();
        Logger.Log("Pipeline.Start: hook armed (device-first resolution, no correlation wait)");
    }

    public void Stop()
    {
        Logger.Log("Pipeline.Stop");
        _hook.Uninstall();
        _hook.ShouldEatVkey = null;
        _hook.ShouldEatVkeyUp = null;
        _rawInputWindow.Stop();

        lock (_hookAteLock)
        {
            _heldSwallowedVkeys.Clear();
        }
    }

    /// <summary>
    /// Called synchronously from the LL hook thread. Must never block: resolves the
    /// source device immediately from whatever raw input is already queued, decides
    /// swallow against ONLY that device's mappings, and — if a match fires — posts
    /// the actual action dispatch to the UI thread (dispatch itself is not time-critical).
    /// </summary>
    private bool OnShouldEatVkey(int vkey)
    {
        if (IsCaptureActive)
            return false;

        var keyName = VirtualKeyToName(vkey);

        if (IsHeldSwallowed(vkey))
        {
            Logger.Log($"SwallowTrace: vkey={vkey} key={keyName} result=EAT reason=heldRepeat");
            return true;
        }

        if (SwallowPlanner.IsModifierKey(keyName))
            return false;

        var startTicks = Environment.TickCount64;
        var devicePath = _resolver.TryResolveDevicePath(vkey, isKeyUp: false);
        var elapsedMs = Environment.TickCount64 - startTicks;
        if (elapsedMs > 5)
            Logger.Log($"HookLatency: OnShouldEatVkey device-resolve took {elapsedMs}ms (should be ~0)");

        if (devicePath is null)
        {
            Logger.Log($"SwallowTrace: vkey={vkey} key={keyName} result=PASS reason=noRawInputQueued");
            return false;
        }

        var pathShort = ShortPath(devicePath);
        var device = DeviceMatcher.FindMatch(_configStore.Current.Devices, devicePath);
        if (device is null)
        {
            Logger.Log($"SwallowTrace: vkey={vkey} key={keyName} path={pathShort} result=PASS reason=unknownDevice");
            return false;
        }

        _triggerMatcher.OnKeyDown(devicePath, keyName);

        var (action, swallow) = ResolveAction(device, devicePath, keyName);
        if (action is null)
        {
            Logger.Log($"MatchTrace: device='{device.Name}' key={keyName} path={pathShort} match=none");
            return false;
        }

        if (IsDebounced(devicePath, vkey))
        {
            Logger.Log($"MatchTrace: device='{device.Name}' key={keyName} action={action.Type} — debounced");
            return swallow;
        }

        Logger.Log($"MatchTrace: device='{device.Name}' key={keyName} action={DescribeAction(action)} swallow={swallow} — dispatch");

        if (swallow)
            MarkHeldSwallowed(vkey);

        DispatchAsync(device, keyName, action);
        return swallow;
    }

    private bool OnShouldEatVkeyUp(int vkey)
    {
        if (IsCaptureActive)
            return false;

        if (ClearHeldSwallowed(vkey))
        {
            Logger.Log($"SwallowTrace: vkey={vkey} key={VirtualKeyToName(vkey)} result=EAT reason=heldUp");
            return true;
        }

        var keyName = VirtualKeyToName(vkey);
        var devicePath = _resolver.TryResolveDevicePath(vkey, isKeyUp: true);
        if (devicePath is not null)
            _triggerMatcher.OnKeyUp(devicePath, keyName);

        return false;
    }

    private void DispatchAsync(DeviceConfig device, string keyName, ActionConfig action)
    {
        void Dispatch()
        {
            try
            {
                Logger.Log($"DispatchTrace: begin device='{device.Name}' key={keyName} action={DescribeAction(action)}");
                _dispatcher.Dispatch(action);
                Logger.Log($"DispatchTrace: ok device='{device.Name}' key={keyName}");
            }
            catch (Exception ex)
            {
                Logger.Log($"DispatchTrace: FAIL device='{device.Name}' key={keyName}: {ex}");
            }
        }

        if (_uiContext is not null)
            _uiContext.Post(_ => Dispatch(), null);
        else
            ThreadPool.QueueUserWorkItem(_ => Dispatch());
    }

    private bool IsDebounced(string devicePath, int vkey)
    {
        var now = Environment.TickCount64;
        var key = (devicePath, vkey);
        lock (_lastDispatchedLock)
        {
            if (_lastDispatchedAtMs.TryGetValue(key, out var lastMs) && now - lastMs < DebounceMs)
                return true;

            _lastDispatchedAtMs[key] = now;
            return false;
        }
    }

    private (ActionConfig? Action, bool Swallow) ResolveAction(DeviceConfig device, string devicePath, string keyNameJustPressed)
    {
        if (device.AnyKeyAction is not null)
            return (device.AnyKeyAction, device.DefaultSwallow);

        var mapping = _triggerMatcher.Match(device, devicePath, keyNameJustPressed);
        if (mapping is null) return (null, false);

        return (mapping.Action, mapping.Swallow ?? device.DefaultSwallow);
    }

    private void MarkHeldSwallowed(int vkey)
    {
        lock (_hookAteLock)
            _heldSwallowedVkeys.Add(vkey);
    }

    private bool IsHeldSwallowed(int vkey)
    {
        lock (_hookAteLock)
            return _heldSwallowedVkeys.Contains(vkey);
    }

    private bool ClearHeldSwallowed(int vkey)
    {
        lock (_hookAteLock)
            return _heldSwallowedVkeys.Remove(vkey);
    }

    private static string VirtualKeyToName(int vkey) => ((System.Windows.Forms.Keys)vkey).ToString();

    private static string ShortPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return "(empty)";

        var hid = path.IndexOf("HID#", StringComparison.OrdinalIgnoreCase);
        if (hid >= 0)
        {
            var slice = path[hid..];
            var end = slice.IndexOf('#', 4);
            if (end < 0) end = Math.Min(slice.Length, 48);
            return slice[..end];
        }

        return path.Length <= 48 ? path : path[^48..];
    }

    private static string DescribeAction(ActionConfig action) =>
        action.Type?.ToLowerInvariant() switch
        {
            "pastetext" => $"pasteText(len={action.Text?.Length ?? 0},method={action.Method})",
            "sendkeys" => $"sendKeys(steps={action.Steps?.Count ?? 0})",
            "launch" => $"launch({action.Path})",
            _ => action.Type ?? "(null)",
        };

    // Test-only seam: exercises the exact same synchronous decision path the LL hook
    // calls, without needing a real Windows hook or focus/window plumbing.
    internal bool TestOnlyShouldEatVkey(int vkeyForKeyA) => OnShouldEatVkey(vkeyForKeyA);

    public void Dispose() => Stop();
}
```

Notes on what was deliberately dropped vs. the old file:
- `RawKeyDown`/`RawKeyUp` public events: removed. Nothing outside `InputPipeline` was consuming them for anything other than diagnostics wiring — confirm via `grep -rn "RawKeyDown\|RawKeyUp" src/Keysist` before deleting; if any XAML code-behind subscribes to these for UI capture display (e.g. `RegisterDevicePage.xaml.cs`, `NewMappingPage.xaml.cs` per the earlier `grep -l hook` results), that call site needs its own event source — see Step 3a below.
- `_correlator`, `_rawArrived`, `CorrelateWaitMs`, `GetRawWaitHandle`, `SignalRawArrived`: deleted — this was the blocking machinery being removed.
- `IsCaptureActive` gate: preserved, same semantics (suppresses remap while a capture UI flow is reading raw keys).

- [ ] **Step 3a: Check for other consumers of the removed events/capture path**

Run: `grep -rn "RawKeyDown\|RawKeyUp\|RawInputPump" src/Keysist/Views src/Keysist/Program.cs`

If `RegisterDevicePage.xaml.cs` or `NewMappingPage.xaml.cs` subscribe to `InputPipeline.RawKeyDown`/`RawKeyUp` for their capture UI (showing "press a key" during device registration), those pages need a **separate, small** raw-input listener for capture display purposes — that UI path doesn't have the swallow-latency constraint (it's UI-only, no hook involved), so it's fine for it to keep using a simple `RawInputWindow`-with-WM_INPUT-style listener. Do not try to route capture UI through `IRawEventResolver` — that interface is for the hook's synchronous decision path only. If such a dependency exists, stop here and report it back before proceeding — it means Task 4's "strip WM_INPUT from RawInputWindow" needs to instead become "add a second lightweight window class for capture use," which changes Task 4's file list. This is a plan-fidelity checkpoint, not a step to route around silently.

- [ ] **Step 4: Delete `IsTentativeSwallowCandidate` from SwallowPlanner.cs**

```csharp
// src/Keysist.Core/Config/SwallowPlanner.cs
namespace Keysist.Config;

public static class SwallowPlanner
{
    public static bool IsModifierKey(string keyName) =>
        keyName.Equals("ControlKey", StringComparison.OrdinalIgnoreCase)
        || keyName.Equals("LControlKey", StringComparison.OrdinalIgnoreCase)
        || keyName.Equals("RControlKey", StringComparison.OrdinalIgnoreCase)
        || keyName.Equals("ShiftKey", StringComparison.OrdinalIgnoreCase)
        || keyName.Equals("LShiftKey", StringComparison.OrdinalIgnoreCase)
        || keyName.Equals("RShiftKey", StringComparison.OrdinalIgnoreCase)
        || keyName.Equals("Menu", StringComparison.OrdinalIgnoreCase)
        || keyName.Equals("LMenu", StringComparison.OrdinalIgnoreCase)
        || keyName.Equals("RMenu", StringComparison.OrdinalIgnoreCase)
        || keyName.Equals("LWin", StringComparison.OrdinalIgnoreCase)
        || keyName.Equals("RWin", StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 5: Confirm LowLevelKeyboardHook.cs needs no changes**

Its callback already just calls `ShouldEatVkey?.Invoke(vkey)`/`ShouldEatVkeyUp?.Invoke(vkey)` synchronously and returns — that contract is unchanged; only what happens *inside* those delegates changed. Read `src/Keysist.Core/Hooking/LowLevelKeyboardHook.cs` to confirm no `RawInputPump`/`InputCorrelator` references exist there (there shouldn't be — verified in Task 1's earlier read).

- [ ] **Step 6: Build**

Run: `dotnet build src/Keysist.Core/Keysist.Core.csproj`
Expected: SUCCESS.

Run: `dotnet build src/Keysist/Keysist.csproj`
Expected: SUCCESS (or reveals the Step 3a dependency — resolve per that step's instructions before continuing).

- [ ] **Step 7: Run the new tests**

Run: `dotnet test src/Keysist.Core.Tests/Keysist.Core.Tests.csproj --filter InputPipelineDeviceResolutionTests`
Expected: both tests PASS.

- [ ] **Step 8: Run full test suite**

Run: `dotnet test src/Keysist.Core.Tests/Keysist.Core.Tests.csproj`
Expected: all PASS.

- [ ] **Step 9: Commit everything from Task 4 + Task 5 together**

```bash
git add -A src/Keysist.Core src/Keysist.Core.Tests src/Keysist
git commit -m "fix: resolve device synchronously before swallow decision, remove blocking correlation wait

Root cause: OnShouldEatVkey busy-waited up to 200ms inside the global
WH_KEYBOARD_LL callback for cross-thread Raw Input correlation, freezing
system-wide keyboard input on ANY device whenever the key name matched a
mapping on ANY device (SwallowPlanner checked across all devices, not the
one that fired). GetRawInputBuffer lets the hook resolve the source device
synchronously and immediately, so mappings are matched against the actual
device first, with zero wait."
```

---

## Task 6: Manual hardware verification + latency logging check

**Files:** none (verification only, plus reading `src/Keysist.Core/Diagnostics/Logger.cs` to confirm log sink location).

- [ ] **Step 1: Read Logger.cs to find where logs land**

Run: `cat src/Keysist.Core/Diagnostics/Logger.cs`
Note the log file path so you know where to tail during manual testing.

- [ ] **Step 2: Run the app and reproduce the original bug report**

Build and run `src/Keysist/Keysist.csproj` (or the launcher). Configure a mapping on device A (e.g. NumPad) for a key, leave device B (e.g. main keyboard) unmapped for that same key.

- [ ] **Step 3: Press the mapped key on the UNMAPPED device B repeatedly and fast**

Expected: no perceptible stall — typing elsewhere continues without the "freeze for a second" the bug report described. Tail the log and confirm `SwallowTrace: ... result=PASS reason=unknownDevice` (or `noRawInputQueued`) appears immediately, and no `HookLatency` line exceeding 5ms appears.

- [ ] **Step 4: Press the mapped key on device A**

Expected: action fires, key is swallowed per config, log shows `MatchTrace: ... dispatch` with the correct device name — device B's press earlier must not have affected this.

- [ ] **Step 5: Confirm the "must refocus to make it work" symptom is gone**

Focus an arbitrary window (e.g. Notepad), press the mapped key without refocusing anything first. Expected: works immediately, no need to click away and back.

- [ ] **Step 6: If any step fails, do not patch around it — return to Phase 1 of systematic-debugging**

This is a hardware-dependent integration point that cannot be fully unit tested; if manual verification surfaces a new issue, treat it as a fresh root-cause investigation (new hypothesis, not a bolt-on fix to this change).

- [ ] **Step 7: Report results back**

No commit for this task (verification only) — report pass/fail per step back to the user/reviewer.
