using System.Text.Json.Serialization;

namespace Valour.Sdk.Models.Embeds.Items;

/// <summary>
/// A horizontal (flex) container for other items.
/// </summary>
public class EmbedRowItem : EmbedItem
{
    public List<EmbedItem> Children { get; set; } = new();

    public override IEnumerable<EmbedItem> EnumerateDescendants() => EnumerateList(Children);

    public override bool TryReplaceDescendant(EmbedItem replacement) => TryReplaceInList(Children, replacement);
}
