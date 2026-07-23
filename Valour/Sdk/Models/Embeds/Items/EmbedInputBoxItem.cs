using System.Text.Json.Serialization;

namespace Valour.Sdk.Models.Embeds.Items;

/// <summary>
/// A single-line text input whose value is collected on form submit.
/// </summary>
public class EmbedInputBoxItem : EmbedItem, IFormInputItem, INamedItem
{
    public string? Value { get; set; }

    public string? Placeholder { get; set; }

    public EmbedTextItem? NameItem { get; set; }

    public bool KeepValueOnUpdate { get; set; } = true;

    [JsonIgnore]
    public override EmbedItemType ItemType => EmbedItemType.InputBox;

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
