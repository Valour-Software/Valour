using Valour.Sdk.E2ee;

namespace Valour.Client.Maui.Storage;

/// <summary>
/// Stores this device's end-to-end encryption keys in the platform's secure
/// storage: the Keychain on Apple platforms and the Keystore on Android.
/// </summary>
public class SecureE2eeKeyStore : IE2eeKeyStore
{
    public async Task<byte[]> GetAsync(string key)
    {
        var value = await SecureStorage.Default.GetAsync(key);
        if (string.IsNullOrEmpty(value))
            return null;

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
        SecureStorage.Default.SetAsync(key, Convert.ToBase64String(value));

    public Task RemoveAsync(string key)
    {
        SecureStorage.Default.Remove(key);
        return Task.CompletedTask;
    }
}
