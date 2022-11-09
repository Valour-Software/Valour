namespace Valour.Api.Items.Messages.Embeds.Items;

public class EmbedDropDownItem : EmbedItem
{
    public string? TextColor { get; set; } = "eeeeee";
    public string? Text { get; set; }

    public EmbedDropDownItem()
    {
        ItemType = EmbedItemType.DropDownItem;
    }

    public EmbedDropDownItem(string text = null, string textColor = null)
    {
        Text = text;
        TextColor = textColor;
        ItemType = EmbedItemType.DropDownItem;
    }
}