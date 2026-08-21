namespace Keysist.Correlation;

public class InputCorrelator
{
    private readonly IClock _clock;
    private readonly long _ttlMs;
    private readonly Dictionary<int, Queue<(string DevicePath, long RecordedAtMs)>> _pending = new();
    private readonly object _lock = new();

    public InputCorrelator(IClock clock, int ttlMs = 15)
    {
        _clock = clock;
        _ttlMs = ttlMs;
    }

    public void RecordRawInput(int vkey, string devicePath)
    {
        lock (_lock)
        {
            if (!_pending.TryGetValue(vkey, out var queue))
            {
                queue = new Queue<(string, long)>();
                _pending[vkey] = queue;
            }
            queue.Enqueue((devicePath, _clock.NowMs()));
        }
    }

    public bool TryCorrelate(int vkey, out string? devicePath)
    {
        lock (_lock)
        {
            devicePath = null;
            if (!_pending.TryGetValue(vkey, out var queue))
                return false;

            var now = _clock.NowMs();
            while (queue.Count > 0)
            {
                var (path, recordedAt) = queue.Dequeue();
                var age = now - recordedAt;
                if (age <= _ttlMs)
                {
                    devicePath = path;
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>How many pending raw events remain for this vkey (for diagnostics).</summary>
    public int PendingCount(int vkey)
    {
        lock (_lock)
        {
            return _pending.TryGetValue(vkey, out var queue) ? queue.Count : 0;
        }
    }
}
