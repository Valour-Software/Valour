using Valour.Sdk.Cdn;
using Valour.Sdk.Models.Embeds.Items;
using Valour.Shared;

namespace Valour.Sdk.Models.Embeds;

/// <summary>
/// Checks that an embed is well formed and only loads media from allowed
/// sources. The server applies this to embeds it can read. Embeds inside
/// encrypted messages and live updates are checked by the recipient's client
/// after decrypting, because the server never sees them.
/// </summary>
public static class EmbedSafety
{
    /// <summary>Checks a complete embed.</summary>
    public static TaskResult Check(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return TaskResult.FromFailure("Embed data is required.");
        if (json.Length > EmbedParser.MaxPayloadLength)
            return TaskResult.FromFailure($"Embed data must be under {EmbedParser.MaxPayloadLength} chars.");

        var embed = EmbedParser.TryParse(json);
        if (embed is null)
            return TaskResult.FromFailure("Embed data is invalid.");

        var valid = EmbedParser.Validate(embed);
        return valid.Success ? CheckMedia(embed.EnumerateItems()) : valid;
    }

    /// <summary>Checks the changed items of a targeted embed update.</summary>
    public static TaskResult CheckItems(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return TaskResult.FromFailure("Changed items data is required.");
        if (json.Length > EmbedParser.MaxPayloadLength)
            return TaskResult.FromFailure($"Changed items data must be under {EmbedParser.MaxPayloadLength} chars.");

        var items = EmbedParser.TryParseItems(json);
        if (items is null)
            return TaskResult.FromFailure("Changed items data is invalid.");

        var valid = EmbedParser.ValidateItems(items);
        return valid.Success ? CheckMedia(items.Concat(items.SelectMany(x => x.EnumerateDescendants()))) : valid;
    }

    private static TaskResult CheckMedia(IEnumerable<EmbedItem> items)
    {
        foreach (var media in items.OfType<EmbedMediaItem>())
        {
            if (media.Attachment is null)
                return TaskResult.FromFailure("Embed media item is missing its attachment.");

            // Inline previews come from links in the message text. An Inline
            // flag inside embed JSON is chosen by its author, so it never
            // relaxes the media checks.
            media.Attachment.Inline = false;

            var result = MediaUriHelper.ScanMediaUri(media.Attachment);
            if (!result.Success)
                return TaskResult.FromFailure($"Embed media item {media.Id} uses a location that is not allowed.");
        }

        return TaskResult.SuccessResult;
    }
}
