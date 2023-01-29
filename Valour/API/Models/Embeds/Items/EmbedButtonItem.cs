using System.Text.Json.Serialization;

namespace Valour.Api.Models.Messages.Embeds.Items;

public class EmbedButtonItem : EmbedItem, IClickable
{
	public EmbedClickTargetBase ClickTarget { get; set; }

	[JsonIgnore]
	public override EmbedItemType ItemType => EmbedItemType.Button;
} 