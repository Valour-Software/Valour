using System.Text.Json.Serialization;
using Valour.Sdk.Models.Messages.Embeds.Styles;

namespace Valour.Sdk.Models.Messages.Embeds.Items;

public class EmbedTextItem : EmbedItem, IClickable, INameable
{
    public EmbedTextItem NameItem { get; set; }
    public string Text { get; set; }
	public EmbedClickTargetBase ClickTarget { get; set; }

	[JsonIgnore]
	public override EmbedItemType ItemType => EmbedItemType.Text;

	public EmbedTextItem()
	{

	}

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

	public EmbedTextItem(string text)
	{
		Text = text;
	}
}