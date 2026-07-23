using System.Text.Json.Serialization;

namespace Valour.Sdk.Models.Embeds.Items;

/// <summary>
/// A piece of media (image, video, etc.) rendered through the normal
/// attachment pipeline. The attachment location must pass the server's
/// media URI scan.
/// </summary>
public class EmbedMediaItem : EmbedItem, IClickableItem
{
    public MessageAttachment? Attachment { get; set; }

    public EmbedClickTarget? ClickTarget { get; set; }

    [JsonIgnore]
    public override EmbedItemType ItemType => EmbedItemType.Media;
}
