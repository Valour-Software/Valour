using Valour.Client.Storage;
using Valour.Shared.Models;

namespace Valour.Client.Utility;

public sealed class AppBannerService
{
    private readonly IAppStorage _storage;

    public AppBannerService(IAppStorage storage)
    {
        _storage = storage;
    }

    public event Action Changed;
    public bool UpdateAvailable { get; private set; }
    public PlatformBanner PlatformBanner { get; private set; }

    public void ShowUpdateAvailable()
    {
        UpdateAvailable = true;
        Changed?.Invoke();
    }

    public void DismissUpdate()
    {
        UpdateAvailable = false;
        Changed?.Invoke();
    }

    public async Task SetPlatformBannerAsync(PlatformBanner banner)
    {
        if (banner is null || string.IsNullOrWhiteSpace(banner.Hash))
        {
            PlatformBanner = null;
            Changed?.Invoke();
            return;
        }

        PlatformBanner = await _storage.ContainsKeyAsync(GetDismissalKey(banner.Hash)) ? null : banner;
        Changed?.Invoke();
    }

    public async Task DismissPlatformBannerAsync()
    {
        if (PlatformBanner is null)
            return;
        await _storage.SetStringAsync(GetDismissalKey(PlatformBanner.Hash), "1");
        PlatformBanner = null;
        Changed?.Invoke();
    }

    private static string GetDismissalKey(string hash) => $"dismissed-platform-banner:{hash}";
}
