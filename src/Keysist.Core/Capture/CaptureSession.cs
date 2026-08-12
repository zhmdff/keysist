namespace Keysist.Capture;

public enum CaptureMode
{
    Key,
    Chord,
    Sequence,
}

public sealed class CaptureResult
{
    public required string DevicePath { get; init; }
    public required CaptureMode Mode { get; init; }
    public required IReadOnlyList<string> Keys { get; init; }
}

public sealed class CaptureSession
{
    private CaptureMode _mode;
    private string? _devicePathFilter;
    private string? _pinnedDevicePath;
    private readonly List<int> _heldVKeys = [];
    private readonly List<string> _sequenceKeys = [];

    public bool IsCapturing { get; private set; }
    public CaptureResult? Result { get; private set; }

    public void Start(CaptureMode mode, string? devicePathFilter = null)
    {
        _mode = mode;
        _devicePathFilter = devicePathFilter;
        _pinnedDevicePath = null;
        _heldVKeys.Clear();
        _sequenceKeys.Clear();
        Result = null;
        IsCapturing = true;
    }

    public void ClearResult()
    {
        Result = null;
        IsCapturing = false;
        _heldVKeys.Clear();
        _sequenceKeys.Clear();
        _pinnedDevicePath = null;
    }

    public void Stop()
    {
        if (!IsCapturing)
            return;

        if (Result is null)
        {
            var keys = CurrentKeys();
            if (keys.Count > 0)
                Result = BuildResult(keys);
        }

        IsCapturing = false;
    }

    public void OnRawKeyDown(int vkey, string devicePath)
    {
        if (!IsCapturing || !AcceptsDevice(devicePath))
            return;

        PinDevice(devicePath);

        switch (_mode)
        {
            case CaptureMode.Key:
                FinalizeResult([VKeyToName(vkey)]);
                break;

            case CaptureMode.Chord:
                if (_heldVKeys.Contains(vkey))
                    break;

                if (_heldVKeys.Count >= 1)
                {
                    _heldVKeys.Add(vkey);
                    FinalizeResult(_heldVKeys.Select(VKeyToName).ToList());
                }
                else
                {
                    _heldVKeys.Add(vkey);
                }
                break;

            case CaptureMode.Sequence:
                _sequenceKeys.Add(VKeyToName(vkey));
                break;
        }
    }

    public void OnRawKeyUp(int vkey, string devicePath)
    {
        if (!IsCapturing || !AcceptsDevice(devicePath))
            return;

        if (_mode == CaptureMode.Chord)
            _heldVKeys.Remove(vkey);
    }

    private bool AcceptsDevice(string devicePath)
    {
        if (_devicePathFilter is not null)
        {
            if (devicePath.Equals(_devicePathFilter, StringComparison.OrdinalIgnoreCase))
                return true;
            return devicePath.Contains(_devicePathFilter, StringComparison.OrdinalIgnoreCase);
        }

        if (_pinnedDevicePath is not null)
            return devicePath == _pinnedDevicePath;

        return true;
    }

    private void PinDevice(string devicePath)
    {
        if (_devicePathFilter is null && _pinnedDevicePath is null)
            _pinnedDevicePath = devicePath;
    }

    private IReadOnlyList<string> CurrentKeys() =>
        _mode switch
        {
            CaptureMode.Sequence => _sequenceKeys,
            CaptureMode.Chord => _heldVKeys.Select(VKeyToName).ToList(),
            _ => [],
        };

    private void FinalizeResult(IReadOnlyList<string> keys)
    {
        if (keys.Count == 0)
            return;

        Result = BuildResult(keys);
        IsCapturing = false;
    }

    private CaptureResult BuildResult(IReadOnlyList<string> keys) =>
        new()
        {
            DevicePath = _devicePathFilter ?? _pinnedDevicePath!,
            Mode = _mode,
            Keys = keys.ToList(),
        };

    private static string VKeyToName(int vkey) =>
        ((System.Windows.Forms.Keys)vkey).ToString();
}
