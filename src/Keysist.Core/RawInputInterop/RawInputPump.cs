using System.Windows.Forms;
using Keysist.Diagnostics;

namespace Keysist.RawInputInterop;

/// <summary>
/// Runs <see cref="RawInputWindow"/> on a dedicated STA thread with its own message pump
/// so WM_INPUT can update correlation state in parallel with the UI-thread LL hook.
/// </summary>
public sealed class RawInputPump : IDisposable
{
    private Thread? _thread;
    private RawInputWindow? _window;
    private SynchronizationContext? _rawContext;
    private readonly ManualResetEventSlim _ready = new(false);
    private volatile bool _stopping;

    public event Action<int, string>? RawKeyDown;
    public event Action<int, string>? RawKeyUp;

    public void Start()
    {
        if (_thread is not null)
            return;

        _stopping = false;
        _thread = new Thread(ThreadMain)
        {
            Name = "Keysist-RawInput",
            IsBackground = true,
            // WM_INPUT must reach this thread with minimal delay -- it's on the critical
            // path of every LL-hook swallow decision. At normal priority, an idle thread
            // can take tens of ms to get rescheduled after a period of inactivity, which
            // is enough to blow the correlation window on a deliberate, unhurried keypress.
            Priority = ThreadPriority.Highest,
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();

        if (!_ready.Wait(TimeSpan.FromSeconds(5)))
            Logger.Log("RawInputPump.Start: timed out waiting for raw-input thread.");
    }

    public void Stop()
    {
        if (_thread is null)
            return;

        _stopping = true;
        var ctx = _rawContext;
        if (ctx is not null)
        {
            try
            {
                ctx.Send(_ =>
                {
                    try
                    {
                        _window?.Dispose();
                        _window = null;
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"RawInputPump.Stop: dispose failed: {ex}");
                    }

                    Application.ExitThread();
                }, null);
            }
            catch (Exception ex)
            {
                Logger.Log($"RawInputPump.Stop: send failed: {ex}");
            }
        }

        _thread.Join(TimeSpan.FromSeconds(2));
        _thread = null;
        _rawContext = null;
        _ready.Reset();
    }

    private void ThreadMain()
    {
        try
        {
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            _rawContext = SynchronizationContext.Current;

            _window = new RawInputWindow();
            _window.RawKeyDown += (vkey, path) =>
            {
                if (!_stopping)
                    RawKeyDown?.Invoke(vkey, path);
            };
            _window.RawKeyUp += (vkey, path) =>
            {
                if (!_stopping)
                    RawKeyUp?.Invoke(vkey, path);
            };
            _window.Start();
            _ready.Set();

            Application.Run();
        }
        catch (Exception ex)
        {
            Logger.Log($"RawInputPump thread crashed: {ex}");
            _ready.Set();
        }
    }

    public void Dispose() => Stop();
}
