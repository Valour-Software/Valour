using System.Text.Json;
using Valour.Client.Storage;

namespace Valour.Client.Photino.Storage;

/// <summary>
/// Keeps app preferences in a JSON file in the user's config directory.
/// Values are written back to disk after every change.
/// </summary>
public sealed class FileAppStorage : IAppStorage
{
    private readonly string _path = Path.Combine(AppPaths.ConfigDirectory, "preferences.json");
    private readonly Lock _lock = new();
    private readonly Dictionary<string, string> _values;

    public FileAppStorage()
    {
        _values = Load();
    }

    public Task<string?> GetStringAsync(string key)
    {
        lock (_lock)
            return Task.FromResult(_values.GetValueOrDefault(key));
    }

    public Task SetStringAsync(string key, string value)
    {
        lock (_lock)
        {
            _values[key] = value;
            Save();
        }
        return Task.CompletedTask;
    }

    public async Task<T?> GetAsync<T>(string key)
    {
        var json = await GetStringAsync(key);
        return json is null ? default : JsonSerializer.Deserialize<T>(json);
    }

    public Task SetAsync<T>(string key, T value) => SetStringAsync(key, JsonSerializer.Serialize(value));

    public Task<bool> ContainsKeyAsync(string key)
    {
        lock (_lock)
            return Task.FromResult(_values.ContainsKey(key));
    }

    public Task RemoveAsync(string key)
    {
        lock (_lock)
        {
            if (_values.Remove(key))
                Save();
        }
        return Task.CompletedTask;
    }

    private Dictionary<string, string> Load()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_path)) ?? [];
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not read {_path}: {ex.Message}");
        }
        return [];
    }

    // Writes a temporary file and renames it, so a crash can't leave half a file.
    private void Save()
    {
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_values));
        File.Move(temp, _path, overwrite: true);
    }
}
