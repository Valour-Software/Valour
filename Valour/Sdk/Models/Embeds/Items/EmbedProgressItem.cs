using System.Text.Json.Serialization;

namespace Valour.Sdk.Models.Messages.Embeds.Items;

public class EmbedProgress : EmbedItem, INameable
{
    public EmbedTextItem NameItem { get; set; }

	[JsonIgnore]
	public override EmbedItemType ItemType => EmbedItemType.Progress;

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

    public override string GetClasses()
    {
        return base.GetClasses()+"progress";
    }
}