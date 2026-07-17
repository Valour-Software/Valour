using Valour.Config.Configs;
using Valour.Shared.Models;

namespace Valour.Server.API;

/// <summary>
/// Serves the instance manifest — a self-description of this deployment's
/// hosts and configured capabilities. Anonymous; read by clients at startup
/// and by federation peers.
/// </summary>
public class InstanceApi
{
    public static void AddRoutes(WebApplication app)
    {
        app.MapGet("/.well-known/valour-instance", GetManifest);
    }

    private static IResult GetManifest()
    {
        var hosting = HostingConfig.Current;

        var manifest = new InstanceManifest
        {
            Name = hosting.InstanceName,
            Version = typeof(ISharedUser).Assembly.GetName().Version?.ToString(),
            IsOfficial = string.Equals(hosting.RootDomain, "valour.gg", StringComparison.OrdinalIgnoreCase),
            Hosts = new InstanceHosts
            {
                RootDomain = hosting.RootDomain,
                App = hosting.AppHost,
                Api = hosting.ApiHost,
                Threads = hosting.ThreadsHost,
                ContentCdn = hosting.ContentCdnHost,
                PublicCdn = hosting.PublicCdnHost,
            },
            Capabilities = new InstanceCapabilities
            {
                Email = EmailConfig.IsEnabled,
                Payments = !string.IsNullOrWhiteSpace(StripeConfig.Current?.SecretKey),
                Voice = VoiceCapable(),
                PushNotifications = PushCapable(),
                MediaSafety = MediaSafetyConfig.Current?.Enabled ?? false,
                OpenRegistration = true,
            },
            DefaultMaxUploadBytes = UserSubscriptionTypes.GetMaxUploadBytes(null),
        };

        return Results.Json(manifest);
    }

    private static bool VoiceCapable()
    {
        var cf = CloudflareConfig.Instance;
        return !string.IsNullOrWhiteSpace(cf?.RealtimeAccountId) &&
               !string.IsNullOrWhiteSpace(cf?.RealtimeAppId) &&
               !string.IsNullOrWhiteSpace(cf?.RealtimeApiToken);
    }

    private static bool PushCapable()
    {
        var config = NotificationsConfig.Current;
        if (config is null)
            return false;

        var hasVapid = !string.IsNullOrWhiteSpace(config.PublicKey) &&
                       !string.IsNullOrWhiteSpace(config.PrivateKey);
        var hasFirebase = !string.IsNullOrWhiteSpace(config.FirebaseCredentialPath);

        return hasVapid || hasFirebase;
    }
}
