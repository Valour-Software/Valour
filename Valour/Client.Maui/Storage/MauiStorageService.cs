using System.Text.Json;
using Valour.Client.Storage;

namespace Valour.Client.Maui.Storage;

public class MauiStorageService : IAppStorage
{
    public Task<string?> GetStringAsync(string key)
    {
        var value = Preferences.Default.Get<string?>(key, null);
        return Task.FromResult(value);
    }

    public Task SetStringAsync(string key, string value)
    {
        Preferences.Default.Set(key, value);
        return Task.CompletedTask;
    }

    public Task<T?> GetAsync<T>(string key)
    {
        var json = Preferences.Default.Get<string?>(key, null);
        if (json is null)
            return Task.FromResult(default(T));
        return Task.FromResult(JsonSerializer.Deserialize<T>(json));
    }

    public Task SetAsync<T>(string key, T value)
    {
        var json = JsonSerializer.Serialize(value);
        Preferences.Default.Set(key, json);
        return Task.CompletedTask;
    }

    public Task<bool> ContainsKeyAsync(string key)
    {
        return Task.FromResult(Preferences.Default.ContainsKey(key));
    }

    public Task RemoveAsync(string key)
    {
        Preferences.Default.Remove(key);
        return Task.CompletedTask;
    }
}
