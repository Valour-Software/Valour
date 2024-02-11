using Valour.Sdk.Models;
using Valour.Shared;

namespace Valour.Server.Cdn;

public class MediaUriHelper
{
    public static readonly Regex AttachmentRejectRegex = new Regex("(^|.)(<|>|\"|'|\\s)(.|$)");

    public static TaskResult ScanMediaUri(MessageAttachment attachment)
    {
        if (!attachment.Location.StartsWith("https://cdn.valour.gg") &&
            !attachment.Location.StartsWith("https://media.tenor.com") &&
            !attachment.Location.StartsWith("https://app.valour.gg"))
        {
            return new(false, "Attachments must be from an allowed source...");
        }
        if (AttachmentRejectRegex.IsMatch(attachment.Location))
        {
            return new(false, "Attachment location contains invalid characters");
        }
        return new(true, "");
    }
}
