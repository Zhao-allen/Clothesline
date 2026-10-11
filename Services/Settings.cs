using System.IO;
using System.Text.Json;

namespace Clothesline.Services;

/// <summary>
/// Persisted settings, replacing macOS UserDefaults. Stored as JSON next to
/// the executable in a simple key/value store.
/// </summary>
internal sealed class Settings
{
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Clothesline");

    private static readonly string FilePath = Path.Combine(Folder, "settings.json");

    private readonly Dictionary<string, JsonElement> _store;

    private static readonly Lazy<Settings> _instance = new(() => new Settings());
    public static Settings Default => _instance.Value;

    private Settings()
    {
        _store = Load();
    }

    private static Dictionary<string, JsonElement> Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var doc = JsonDocument.Parse(json);
                var dict = new Dictionary<string, JsonElement>();
                foreach (var p in doc.RootElement.EnumerateObject())
                    dict[p.Name] = p.Value.Clone();
                return dict;
            }
        }
        catch { /* corrupt settings: start fresh */ }
        return new Dictionary<string, JsonElement>();
    }

    private readonly object _lock = new();

    public void Save()
    {
        lock (_lock)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                var obj = new Dictionary<string, object>();
                foreach (var kv in _store)
                    obj[kv.Key] = kv.Value;
                var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(FilePath, json);
            }
            catch { /* best effort */ }
        }
    }

    public bool GetBool(string key, bool defaultValue = false)
    {
        lock (_lock)
        {
            if (_store.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.True) return true;
            if (v.ValueKind == JsonValueKind.False) return false;
            return defaultValue;
        }
    }

    public void SetBool(string key, bool value)
    {
        lock (_lock)
        {
            _store[key] = JsonSerializer.SerializeToElement(value);
        }
        Save();
    }

    public int GetInt(string key, int defaultValue = 0)
    {
        lock (_lock)
        {
            if (_store.TryGetValue(key, out var v) && v.TryGetInt32(out var i)) return i;
            return defaultValue;
        }
    }

    public void SetInt(string key, int value)
    {
        lock (_lock)
        {
            _store[key] = JsonSerializer.SerializeToElement(value);
        }
        Save();
    }

    public string? GetString(string key)
    {
        lock (_lock)
        {
            if (_store.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString();
            return null;
        }
    }

    public void SetString(string key, string? value)
    {
        lock (_lock)
        {
            if (value is null) _store.Remove(key);
            else _store[key] = JsonSerializer.SerializeToElement(value);
        }
        Save();
    }

    public List<string> GetStringList(string key)
    {
        lock (_lock)
        {
            if (_store.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.Array)
            {
                var list = new List<string>();
                foreach (var el in v.EnumerateArray())
                    if (el.ValueKind == JsonValueKind.String) list.Add(el.GetString()!);
                return list;
            }
            return new List<string>();
        }
    }

    public void SetStringList(string key, IEnumerable<string> values)
    {
        lock (_lock)
        {
            _store[key] = JsonSerializer.SerializeToElement(values.ToList());
        }
        Save();
    }
}
