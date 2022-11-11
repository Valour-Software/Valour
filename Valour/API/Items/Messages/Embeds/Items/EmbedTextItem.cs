namespace Valour.Api.Items.Messages.Embeds.Items;

public class EmbedTextItem : EmbedItem
{
    public string? Name { get; set; }
    public string? TextColor { get; set; } = "eeeeee";
    public string? Link { get; set; }
    public string? Text { get; set; }
    public bool? IsNameClickable { get; set; }

    public EmbedTextItem()
    {
        ItemType = EmbedItemType.Text;
    }

    public EmbedTextItem(string name = null, string text = null, string textColor = null, string link = null, bool? isnameclickable = null, string? onclickeventname = null, int? x = null, int? y = null)
    {
        Name = name;
        Text = text;
        TextColor = textColor;
        Link = link;
        X = x;
        Y = y;
        ItemType = EmbedItemType.Text;
        IsNameClickable = isnameclickable;
        OnClickEventName = onclickeventname;
    }
}