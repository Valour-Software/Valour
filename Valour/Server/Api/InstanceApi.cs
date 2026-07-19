using Valour.Config.Configs;
using Valour.Server.Services;
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

    private static IResult GetManifest(IVoiceProvider voiceProvider)
    {
        var hosting = HostingConfig.Current;

        var voiceConfigured = voiceProvider.IsConfigured;
        var voiceEndpoint = voiceConfigured && voiceProvider.Kind == VoiceProvider.LiveKit
            ? VoiceConfig.Current?.LiveKitUrl ?? string.Empty
            : string.Empty;

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
                Docs = hosting.WikiHost,
                ContentCdn = hosting.ContentCdnHost,
                PublicCdn = hosting.PublicCdnHost,
            },
            Capabilities = new InstanceCapabilities
            {
                Email = EmailConfig.IsEnabled,
                Payments = !string.IsNullOrWhiteSpace(StripeConfig.Current?.SecretKey),
                Voice = voiceConfigured,
                VoiceProvider = voiceConfigured ? voiceProvider.Kind.ToWire() : VoiceProvider.None.ToWire(),
                VoiceEndpoint = voiceEndpoint,
                PushNotifications = PushCapable(),
                MediaSafety = MediaSafetyConfig.Current?.Enabled ?? false,
                OpenRegistration = true,
                FederationHub = FederationHubService.HubEnabled,
                FederationNode = FederationNodeService.NodeEnabled,
            },
            DefaultMaxUploadBytes = UserSubscriptionTypes.GetMaxUploadBytes(null),
        };

        return Results.Json(manifest);
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
