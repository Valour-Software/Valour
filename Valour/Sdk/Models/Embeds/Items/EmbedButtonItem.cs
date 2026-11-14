using System.Text.Json.Serialization;

namespace Valour.Sdk.Models.Embeds.Items;

/// <summary>
/// A clickable button. Its label is whatever items it contains.
/// </summary>
public class EmbedButtonItem : EmbedItem, IClickableItem
{
    public List<EmbedItem> Children { get; set; } = new();

    public EmbedClickTarget? ClickTarget { get; set; }

    public override IEnumerable<EmbedItem> EnumerateDescendants() => EnumerateList(Children);

    public override bool TryReplaceDescendant(EmbedItem replacement) => TryReplaceInList(Children, replacement);
}
