using System.Text.RegularExpressions;
using Valour.Sdk.Models;
using Valour.Shared;
using Valour.Shared.Cdn;
using Valour.Shared.Hosting;
using Valour.Shared.Models;

namespace Valour.Sdk.Cdn;

/// <summary>
/// Decides which media locations messages and embeds may load. Media outside
/// these sources would load directly from a third party and reveal the
/// reader's IP address. The server applies it to attachments it can see, and
/// clients apply it to embeds inside encrypted messages.
/// </summary>
public static class MediaUriHelper
{
    public static readonly Regex AttachmentRejectRegex = new Regex("(^|.)(<|>|\"|'|\\s)(.|$)");

    public static TaskResult ScanMediaUri(MessageAttachment attachment)
    {
        if (string.IsNullOrWhiteSpace(attachment.Location))
            return new(false, "Attachment location is required");

        if (AttachmentRejectRegex.IsMatch(attachment.Location))
            return new(false, "Attachment location contains invalid characters");

        if (!IsAllowedLocation(attachment))
        {
            return new(false, "Attachments must be from an allowed source...");
        }

        return new(true, "");
    }

    private static bool IsAllowedLocation(MessageAttachment attachment)
    {
        if (!Uri.TryCreate(attachment.Location, UriKind.Absolute, out var uri))
            return false;

        // Clients render virtual attachment locations straight into iframes,
        // so every attachment is held to a web scheme and a known host. The
        // one relaxation is plain http for inline previews of provider links
        // (the server builds those from message content and never trusts a
        // client-supplied Inline flag); the host checks below still apply.
        var isHttps = uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        var isInlineHttp = attachment.Inline &&
                           CdnUtils.IsVirtualAttachmentType(attachment.Type) &&
                           uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);
        if (!isHttps && !isInlineHttp)
            return false;

        var host = NormalizeHost(uri.Host);

        if (MatchesConfiguredOrigin(uri, ValourHosts.ContentCdnHost) ||
            MatchesConfiguredOrigin(uri, ValourHosts.AppHost) ||
            host == "media.tenor.com" ||
            KlipyMediaUrls.IsAllowed(attachment.Location))
            return true;

        if (CdnUtils.TryGetVirtualAttachmentType(host, out var mappedType) &&
            mappedType == attachment.Type)
        {
            return true;
        }

        return attachment.Type switch
        {
            MessageAttachmentType.YouTube => host == "youtube.com",
            MessageAttachmentType.Vimeo => host == "player.vimeo.com",
            MessageAttachmentType.Twitch => host is "player.twitch.tv" or "clips.twitch.tv",
            MessageAttachmentType.Bluesky => host == "embed.bsky.app",
            _ => false
        };
    }

    public static bool MatchesConfiguredOrigin(Uri location, string configuredHost)
    {
        return Uri.TryCreate($"https://{configuredHost}", UriKind.Absolute, out var configured) &&
               location.Scheme.Equals(configured.Scheme, StringComparison.OrdinalIgnoreCase) &&
               NormalizeHost(location.Host).Equals(NormalizeHost(configured.Host), StringComparison.OrdinalIgnoreCase) &&
               location.Port == configured.Port &&
               string.IsNullOrEmpty(location.UserInfo);
    }

    private static string NormalizeHost(string host)
    {
        if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            return host[4..];

        return host;
    }
}
