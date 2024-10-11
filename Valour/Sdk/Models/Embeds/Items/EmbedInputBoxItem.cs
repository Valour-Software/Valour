using System.Text.Json.Serialization;

namespace Valour.Sdk.Models.Messages.Embeds.Items;

public class EmbedInputBoxItem : EmbedItem, IEmbedFormItem, INameable
{
    /// <summary>
    /// The input value
    /// </summary>
    public string Value { get; set; }

    /// <summary>
    /// The placeholder text for inputs
    /// </summary>
    public string Placeholder { get; set; }

    public new string Id { get; set; }

    public EmbedTextItem NameItem { get; set; }

	public bool? KeepValueOnUpdate { get; set; } = true;

	[JsonIgnore]
	public override EmbedItemType ItemType => EmbedItemType.InputBox;

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

	public EmbedInputBoxItem()
    {
        
    }

    public EmbedInputBoxItem(string id, string value = null, string placeholder = null, bool? keepvalueonupdate = null)
    {
        Id = id;
        Value = value;
        Placeholder = placeholder; 
        KeepValueOnUpdate = keepvalueonupdate;
    }
}