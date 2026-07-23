using System.Text.Json.Serialization;

namespace Valour.Sdk.Models.Embeds.Items;

/// <summary>
/// A piece of text. Rendered as markdown. Optionally clickable and named.
/// </summary>
public class EmbedTextItem : EmbedItem, IClickableItem, INamedItem
{
    public string? Text { get; set; }

    public EmbedTextItem? NameItem { get; set; }

    public EmbedClickTarget? ClickTarget { get; set; }

    [JsonIgnore]
    public override EmbedItemType ItemType => EmbedItemType.Text;

    public EmbedTextItem() { }

    public EmbedTextItem(string? text)
    {
        Text = text;
    }

    public override IEnumerable<EmbedItem> EnumerateDescendants()
    {
        if (NameItem is null)
            yield break;

        yield return NameItem;
        foreach (var descendant in NameItem.EnumerateDescendants())
            yield return descendant;
    }

    public override bool TryReplaceDescendant(EmbedItem replacement)
    {
        if (NameItem?.Id is not null && NameItem.Id == replacement.Id && replacement is EmbedTextItem text)
        {
            NameItem = text;
            return true;
        }

        return NameItem?.TryReplaceDescendant(replacement) ?? false;
    }
}
