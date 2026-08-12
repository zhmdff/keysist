using System.IO;
using System.Text.Json;

namespace Keysist.Config;

public class ConfigStore
{
    private readonly string _filePath;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
    private FileSystemWatcher? _watcher;
    private System.Threading.Timer? _debounceTimer;

    public KeysistConfig Current { get; private set; } = new();
    public event Action<string>? LoadFailed;
    public event Action<string>? SaveFailed;

    // Guards against the FileSystemWatcher reacting to a write this SAME ConfigStore
    // instance just made: without this, Save() -> watcher fires -> DebouncedLoad() ->
    // Load() replaces Current with a freshly-deserialized object graph, orphaning any
    // references open UI windows (EditMappingsWindow's TreeView tags, etc.) still hold.
    private volatile bool _suppressNextWatcherReload;

    public ConfigStore(string filePath)
    {
        _filePath = filePath;
    }

    public void Load()
    {
        if (!File.Exists(_filePath))
        {
            Current = new KeysistConfig();
            return;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var parsed = JsonSerializer.Deserialize<KeysistConfig>(json, _jsonOptions);
            if (parsed is null) throw new JsonException("Deserialized to null");
            Current = parsed;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            LoadFailed?.Invoke(ex.Message);
        }
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath)!;
            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true });
            _suppressNextWatcherReload = true;
            File.WriteAllText(_filePath, json);
            // Windows often raises two Changed events per write; keep suppress briefly
            // so a second event does not reload and orphan in-memory edits.
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                await System.Threading.Tasks.Task.Delay(500);
                _suppressNextWatcherReload = false;
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _suppressNextWatcherReload = false;
            SaveFailed?.Invoke(ex.Message);
        }
    }

    public void StartWatching()
    {
        var dir = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(dir);
        _watcher = new FileSystemWatcher(dir, Path.GetFileName(_filePath))
        {
            EnableRaisingEvents = true
        };
        _watcher.Changed += (_, _) => DebouncedLoad();
        _watcher.Created += (_, _) => DebouncedLoad();
    }

    public void StopWatching()
    {
        _watcher?.Dispose();
        _watcher = null;
        _debounceTimer?.Dispose();
        _debounceTimer = null;
    }

    private void DebouncedLoad()
    {
        // Save() holds suppress for a short window covering duplicate Changed events.
        if (_suppressNextWatcherReload)
            return;

        _debounceTimer?.Dispose();
        _debounceTimer = new System.Threading.Timer(_ => Load(), null, 300, System.Threading.Timeout.Infinite);
    }
}
