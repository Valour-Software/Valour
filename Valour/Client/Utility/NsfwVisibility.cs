using Valour.Client.Device;
using Valour.Sdk.Client;

namespace Valour.Client.Utility;

/// <summary>
/// Decides whether NSFW planets, channels and threads are hidden. Only the iOS
/// app hides them, and only while the user's ShowNsfw preference is off. That
/// preference can only be turned on outside the iOS app, so the iOS app reads it
/// once from the bootstrap data.
/// </summary>
public static class NsfwVisibility
{
    public static bool HidesNsfw(this ValourClient client) =>
        DeviceInfo.IsIosApp && client.StartupPreferences?.ShowNsfw != true;
}
