using Valour.Api.Models;
using Valour.Shared;

namespace Valour.Server.Cdn;

public class MediaUriHelper
{
    public static Regex _attachmentRejectRegex = new Regex("(^|.)(<|>|\"|'|\\s)(.|$)");

    public static TaskResult ScanMediaUri(MessageAttachment attachment)
    {
        if (!attachment.Location.StartsWith("https://cdn.valour.gg") &&
                        !attachment.Location.StartsWith("https://media.tenor.com"))
        {
            return new(false, "Attachments must be from https://cdn.valour.gg...");
        }
        if (_attachmentRejectRegex.IsMatch(attachment.Location))
        {
            return new(false, "Attachment location contains invalid characters");
        }
        return new(true, "");
    }
}
