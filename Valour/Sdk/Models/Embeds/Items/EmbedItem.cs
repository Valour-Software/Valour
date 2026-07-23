using System.Text.Json.Serialization;

namespace Valour.Sdk.Models.Embeds.Items;

public enum EmbedItemType
{
    Row = 1,
    Text = 2,
    Button = 3,
    InputBox = 4,
    DropDown = 5,
    DropDownOption = 6,
    Form = 7,
    Progress = 8,
    ProgressBar = 9,
    Media = 10,
}

/// <summary>
/// An item that performs an action when clicked.
/// </summary>
public interface IClickableItem
{
    EmbedClickTarget? ClickTarget { get; set; }
}

/// <summary>
/// An item that can display a bolded name above its content.
/// </summary>
public interface INamedItem
{
    EmbedTextItem? NameItem { get; set; }
}

/// <summary>
/// An item whose value is collected when an enclosing form is submitted.
/// </summary>
public interface IFormInputItem
{
    string? Id { get; set; }
    string? Value { get; set; }

    /// <summary>
    /// When true (the default), a live embed update does not overwrite
    /// what the user has already entered.
    /// </summary>
    bool KeepValueOnUpdate { get; set; }

    EmbedItemType ItemType { get; }
}

/// <summary>
/// Base type for everything that can appear inside an embed page.
/// The tree is purely serializable: items hold no parent or embed
/// back-references; render-time context flows through the component tree.
/// </summary>
[JsonDerivedType(typeof(EmbedRowItem), "row")]
[JsonDerivedType(typeof(EmbedTextItem), "text")]
[JsonDerivedType(typeof(EmbedButtonItem), "button")]
[JsonDerivedType(typeof(EmbedInputBoxItem), "input")]
[JsonDerivedType(typeof(EmbedDropDownItem), "dropdown")]
[JsonDerivedType(typeof(EmbedDropDownOptionItem), "option")]
[JsonDerivedType(typeof(EmbedFormItem), "form")]
[JsonDerivedType(typeof(EmbedProgressItem), "progress")]
[JsonDerivedType(typeof(EmbedProgressBarItem), "progressBar")]
[JsonDerivedType(typeof(EmbedMediaItem), "media")]
public abstract class EmbedItem
{
    /// <summary>
    /// Optional identifier. Required for items that participate in forms,
    /// interaction events, or targeted live updates.
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// Inline CSS declarations applied to the item, e.g. "width: 50%; color: red;".
    /// Build with <see cref="Styles.EmbedStyles"/> for typed authoring.
    /// </summary>
    public string? Style { get; set; }

    /// <summary>
    /// Extra CSS classes applied to the item, space-separated.
    /// </summary>
    public string? Classes { get; set; }

    [JsonIgnore]
    public abstract EmbedItemType ItemType { get; }

    /// <summary>
    /// Enumerates all items nested under this one, depth-first.
    /// </summary>
    public virtual IEnumerable<EmbedItem> EnumerateDescendants()
    {
        yield break;
    }

    /// <summary>
    /// Replaces the descendant whose Id matches the replacement's Id.
    /// Returns true if a swap occurred.
    /// </summary>
    public virtual bool TryReplaceDescendant(EmbedItem replacement) => false;

    /// <summary>
    /// Enumerates a child list and everything below it, depth-first.
    /// </summary>
    protected static IEnumerable<EmbedItem> EnumerateList(IEnumerable<EmbedItem>? children)
    {
        if (children is null)
            yield break;

        foreach (var child in children)
        {
            yield return child;
            foreach (var descendant in child.EnumerateDescendants())
                yield return descendant;
        }
    }

    /// <summary>
    /// Replaces an item by Id within a child list (searching nested items too).
    /// Returns true if a swap occurred.
    /// </summary>
    protected static bool TryReplaceInList(List<EmbedItem>? children, EmbedItem replacement)
    {
        if (children is null)
            return false;

        for (var i = 0; i < children.Count; i++)
        {
            if (children[i].Id is not null && children[i].Id == replacement.Id)
            {
                children[i] = replacement;
                return true;
            }

            if (children[i].TryReplaceDescendant(replacement))
                return true;
        }

        return false;
    }
}
