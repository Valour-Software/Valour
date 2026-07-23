using System.Text.Json.Serialization;

namespace Valour.Sdk.Models.Embeds.Items;

/// <summary>
/// A dropdown menu whose selected value is collected on form submit.
/// </summary>
public class EmbedDropDownItem : EmbedItem, IFormInputItem, INamedItem
{
    /// <summary>
    /// The currently selected value.
    /// </summary>
    public string? Value { get; set; }

    public EmbedTextItem? NameItem { get; set; }

    public bool KeepValueOnUpdate { get; set; } = true;

    public List<EmbedDropDownOptionItem> Options { get; set; } = new();

    [JsonIgnore]
    public override EmbedItemType ItemType => EmbedItemType.DropDown;

    public override IEnumerable<EmbedItem> EnumerateDescendants()
    {
        if (NameItem is not null)
        {
            yield return NameItem;
            foreach (var descendant in NameItem.EnumerateDescendants())
                yield return descendant;
        }

        foreach (var option in Options)
            yield return option;
    }

    public override bool TryReplaceDescendant(EmbedItem replacement)
    {
        if (NameItem?.Id is not null && NameItem.Id == replacement.Id && replacement is EmbedTextItem text)
        {
            NameItem = text;
            return true;
        }

        if (replacement is EmbedDropDownOptionItem option)
        {
            for (var i = 0; i < Options.Count; i++)
            {
                if (Options[i].Id is not null && Options[i].Id == option.Id)
                {
                    Options[i] = option;
                    return true;
                }
            }
        }

        return false;
    }
}

/// <summary>
/// A selectable option within a dropdown.
/// </summary>
public class EmbedDropDownOptionItem : EmbedItem
{
    /// <summary>
    /// The text displayed for this option.
    /// </summary>
    public string? Text { get; set; }

    /// <summary>
    /// The value submitted when this option is selected. Defaults to <see cref="Text"/>.
    /// </summary>
    public string? Value { get; set; }

    [JsonIgnore]
    public string? EffectiveValue => Value ?? Text;

    [JsonIgnore]
    public override EmbedItemType ItemType => EmbedItemType.DropDownOption;
}
