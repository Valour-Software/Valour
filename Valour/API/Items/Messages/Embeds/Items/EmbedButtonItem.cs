namespace Valour.Api.Items.Messages.Embeds.Items;

public class EmbedButtonItem : EmbedItem
{
    public string Id { get; set; }
    public string TextColor { get; set; } = "eeeeee";
    public string Color { get; set; } = "000000";
    public EmbedItemSize? Size { get; set; } = EmbedItemSize.Normal;
    public string Text { get; set; }
    public string Event { get; set; }
    public bool IsSubmitButton { get; set; } = false;

    public EmbedButtonItem()
    {
        ItemType = EmbedItemType.Button;
    }

    public EmbedButtonItem(string id = null, string text = null, string textColor = null, string color = null, string itemEvent = null, EmbedItemSize size = EmbedItemSize.Normal, int? x = null, int? y = null, bool isSubmitButton = false)
    {
        Id = id;
        Text = text;
        TextColor = textColor;
        Color = color;
        Size = size;
        IsSubmitButton = isSubmitButton;
        X = x;
        Y = y;
        Event = itemEvent;
        ItemType = EmbedItemType.Button;
    }
}