// src/Keysist.Core/RawInputInterop/RawInputTargetWindow.cs
namespace Keysist.RawInputInterop;

/// <summary>
/// A hidden message-only window that exists solely to satisfy RegisterRawInputDevices'
/// target-window requirement for the hook's synchronous device resolution path. Its
/// WndProc does not (and must not) handle WM_INPUT: raw input for this target is drained
/// synchronously elsewhere via RawInputBufferReader.TryResolveDevicePath (GetRawInputBuffer),
/// never via WM_INPUT message dispatch — mixing the two drain mechanisms on the same window
/// would race (GetRawInputBuffer can remove queue entries a pending WM_INPUT still expects).
///
/// The capture UI (see CapturePanel) has no such latency constraint and keeps using the
/// older WM_INPUT-driven <see cref="RawInputWindow"/>/<see cref="RawInputPump"/> pair
/// instead — the two mechanisms are intentionally kept on separate registrations, swapped
/// in and out (see InputPipeline.ReclaimRawInputRegistration) rather than shared.
/// </summary>
public class RawInputTargetWindow : System.Windows.Forms.NativeWindow, IDisposable
{
    public void Start()
    {
        CreateHandle(new System.Windows.Forms.CreateParams
        {
            Caption = "KeysistRawInputTarget",
            Parent = new IntPtr(-3) // HWND_MESSAGE
        });
    }

    public void Stop() => DestroyHandle();

    public void Dispose() => Stop();
}
