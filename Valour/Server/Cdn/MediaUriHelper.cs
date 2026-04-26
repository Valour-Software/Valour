using Valour.Sdk.Models;
using Valour.Shared;
using Valour.Shared.Cdn;
using Valour.Shared.Models;

namespace Valour.Server.Cdn;

public class MediaUriHelper
{
    public static readonly Regex AttachmentRejectRegex = new Regex("(^|.)(<|>|\"|'|\\s)(.|$)");

    public static TaskResult ScanMediaUri(MessageAttachment attachment)
    {
        if (string.IsNullOrWhiteSpace(attachment.Location))
            return new(false, "Attachment location is required");

        if (AttachmentRejectRegex.IsMatch(attachment.Location))
            return new(false, "Attachment location contains invalid characters");

        if (attachment.Inline && CdnUtils.IsVirtualAttachmentType(attachment.Type))
            return new(true, "");

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

        if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return false;

        var host = NormalizeHost(uri.Host);

        if (host is "cdn.valour.gg" or "media.tenor.com" or "app.valour.gg")
            return true;

        if (CdnUtils.VirtualAttachmentMap.TryGetValue(host, out var mappedType) &&
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

    private static string NormalizeHost(string host)
    {
        if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            return host[4..];

        return host;
    }
}
