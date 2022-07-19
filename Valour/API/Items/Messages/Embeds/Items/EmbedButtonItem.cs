namespace Valour.Api.Items.Messages.Embeds.Items;

public class EmbedButtonItem : EmbedItem
{
    public string? Id { get; set; }
    public string? TextColor { get; set; } = "eeeeee";
    public string? Color { get; set; } = "000000";
    public EmbedItemSize? Size { get; set; } = EmbedItemSize.Normal;
    public string Text { get; set; }
    public string? Link { get; set; }
    public string Event { get; set; }

    /// <summary>
    /// The page number that the embed will be set to when a user clicks this button
    /// </summary>
    public int? GoToPage { get; set; }
    public bool IsSubmitButton { get; set; } = false;

    public EmbedButtonItem(string id = null, string text = null, string textColor = null, string color = null, string link = null, string itemEvent = null, EmbedItemSize size = EmbedItemSize.Normal, int x = 0, int y = 0, bool isSubmitButton = false)
    {
        Id = id;
        Text = text;
        TextColor = textColor;
        Color = color;
        Link = link;
        Size = size;
        IsSubmitButton = isSubmitButton;
        X = x;
        Y = y;
        Event = itemEvent;
    }
}