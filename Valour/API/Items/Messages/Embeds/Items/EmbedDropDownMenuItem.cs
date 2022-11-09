using System.Text.Json.Nodes;

namespace Valour.Api.Items.Messages.Embeds.Items;

public class EmbedDropDownMenuItem : EmbedItem, IEmbedFormItem
{
    /// <summary>
    /// The drop down items in this dropdown
    /// </summary>
    public List<EmbedDropDownItem>? Items { get; set; }

    /// <summary>
    /// the id of this dropdown, ex "Color-picker"
    /// </summary>
    public string Id { get; set; }

    public string Value { get; set; }

    public EmbedDropDownMenuItem()
    {
        ItemType = EmbedItemType.DropDownMenu;
        Items = new();
	}

    public EmbedDropDownMenuItem(string id, int? x = null, int? y = null)
    {
        Id = id;
        ItemType = EmbedItemType.DropDownMenu;
        X = x;
        Y = y;
		Items = new();
	}
}