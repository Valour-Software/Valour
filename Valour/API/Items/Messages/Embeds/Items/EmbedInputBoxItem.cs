namespace Valour.Api.Items.Messages.Embeds.Items;

public class EmbedInputBoxItem : EmbedItem, IEmbedFormItem
{
    /// <summary>
    /// The input value
    /// </summary>
    public string Value { get; set; }

    /// <summary>
    /// The placeholder text for inputs
    /// </summary>
    public string Placeholder { get; set; }
    public string Id { get; set; }

    public string Name { get; set; }

    public string NameColor { get; set; } = "eeeeee";

    public EmbedItemSize Size { get; set; }

    public bool? KeepValueOnUpdate { get; set; } = true;

    public EmbedInputBoxItem()
    {
        ItemType = EmbedItemType.InputBox;
    }

    public EmbedInputBoxItem(string id, string value = null, string placeholder = null, string namecolor = null, bool? keepvalueonupdate = null, int? x = null, int? y = null)
    {
        Id = id;
        Value = value;
        Placeholder = placeholder; 
        X = x;
        Y = y;
        ItemType = EmbedItemType.InputBox;
        NameColor = namecolor;
        KeepValueOnUpdate = keepvalueonupdate;
    }
}