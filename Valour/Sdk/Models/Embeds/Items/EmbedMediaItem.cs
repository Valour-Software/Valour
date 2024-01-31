using System.Text.Json.Serialization;

namespace Valour.Sdk.Models.Messages.Embeds.Items;

public class EmbedMediaItem : EmbedItem, IClickable
{
	[JsonIgnore]
	public override EmbedItemType ItemType => EmbedItemType.Media;

	public MessageAttachment Attachment { get; set; }

	[JsonPropertyName("ct")]
	public EmbedClickTargetBase ClickTarget { get; set; }
}