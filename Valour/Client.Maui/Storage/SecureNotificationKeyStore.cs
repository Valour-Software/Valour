using Valour.Sdk.E2ee;

namespace Valour.Client.Maui.Storage;

/// <summary>
/// Keeps notification keys in the platform's secure storage, where the push
/// handler reads them to show message text while the app is closed.
/// </summary>
public class SecureNotificationKeyStore : INotificationKeyStore
{
    public const string StorageKey = "valour-notification-keys";

    public Task<string> LoadAsync() => SecureStorage.Default.GetAsync(StorageKey);

    public Task SaveAsync(string keySet) => SecureStorage.Default.SetAsync(StorageKey, keySet);

    public Task ClearAsync()
    {
        SecureStorage.Default.Remove(StorageKey);
        return Task.CompletedTask;
    }
}
