using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Valour.Api.Models.Messages.Embeds.Styles;

namespace Valour.Api.Models.Messages.Embeds.Items;

public class EmbedDropDownMenuItem : EmbedItem, IEmbedFormItem, INameable
{

    /// <summary>
    /// the id of this dropdown, ex "Color-picker"
    /// </summary>
    public string Id { get; set; }

	public string Value { get; set; }

	public EmbedTextItem NameItem { get; set; }

	[JsonIgnore]
	public override EmbedItemType ItemType => EmbedItemType.DropDownMenu;
}