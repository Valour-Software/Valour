using Valour.Sdk.E2ee;

namespace Valour.Client.Storage;

/// <summary>
/// Stores this device's end-to-end encryption keys in app storage. In the
/// browser that is the site's local storage, which holds the login token too,
/// so the keys have the same protection as the session itself.
/// </summary>
public class AppStorageE2eeKeyStore : IE2eeKeyStore
{
    private readonly IAppStorage _storage;

    public AppStorageE2eeKeyStore(IAppStorage storage)
    {
        _storage = storage;
    }

    public async Task<byte[]> GetAsync(string key)
    {
        var value = await _storage.GetStringAsync(key);
        if (string.IsNullOrEmpty(value))
            return null;

        // A damaged value is treated as missing rather than failing every
        // key lookup.
        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public Task SetAsync(string key, byte[] value) =>
        _storage.SetStringAsync(key, Convert.ToBase64String(value));

    public Task RemoveAsync(string key) => _storage.RemoveAsync(key);
}
