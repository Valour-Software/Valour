using System.Text.Json.Serialization;

namespace Valour.Api.Models.Messages.Embeds.Items;

public class EmbedDropDownItem : EmbedItem
{
	[JsonIgnore]
	public override EmbedItemType ItemType => EmbedItemType.DropDownItem;
}