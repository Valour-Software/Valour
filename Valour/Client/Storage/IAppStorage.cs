namespace Valour.Client.Storage;

public interface IAppStorage
{
    Task<string?> GetStringAsync(string key);
    Task SetStringAsync(string key, string value);
    Task<T?> GetAsync<T>(string key);
    Task SetAsync<T>(string key, T value);
    Task<bool> ContainsKeyAsync(string key);
    Task RemoveAsync(string key);
}
