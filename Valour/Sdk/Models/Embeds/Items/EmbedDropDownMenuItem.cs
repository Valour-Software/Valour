using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Valour.Sdk.Models.Messages.Embeds.Styles;

namespace Valour.Sdk.Models.Messages.Embeds.Items;

public class EmbedDropDownMenuItem : EmbedItem, IEmbedFormItem, INameable
{

    /// <summary>
    /// the id of this dropdown, ex "Color-picker"
    /// </summary>
    public new string Id { get; set; }

	public string Value { get; set; }

	public EmbedTextItem NameItem { get; set; }

	[JsonIgnore]
	public override EmbedItemType ItemType => EmbedItemType.DropDownMenu;

    public override List<EmbedItem> GetAllItems()
	{
        if (Children is null)
            return new();
        List<EmbedItem> items = new();
		if (NameItem is not null) {
		    items.Add(NameItem);
		    items.AddRange(NameItem.GetAllItems());
        }
        foreach(var _item in Children) 
        {
            items.Add(_item);
            items.AddRange(_item.GetAllItems());
        }
        return items;
	}
}