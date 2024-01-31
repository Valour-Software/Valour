using System.Text.Json.Serialization;

namespace Valour.Sdk.Models.Messages.Embeds.Items;

public class EmbedDropDownItem : EmbedItem
{
	[JsonIgnore]
	public override EmbedItemType ItemType => EmbedItemType.DropDownItem;
}