using System.Text.Json.Serialization;

namespace Valour.Sdk.Models.Embeds.Items;

/// <summary>
/// A progress track containing one or more bars (bootstrap progress).
/// </summary>
public class EmbedProgressItem : EmbedItem, INamedItem
{
    public EmbedTextItem? NameItem { get; set; }

    public List<EmbedProgressBarItem> Bars { get; set; } = new();

    [JsonIgnore]
    public override EmbedItemType ItemType => EmbedItemType.Progress;

    public override IEnumerable<EmbedItem> EnumerateDescendants()
    {
        if (NameItem is not null)
        {
            yield return NameItem;
            foreach (var descendant in NameItem.EnumerateDescendants())
                yield return descendant;
        }

        foreach (var bar in Bars)
            yield return bar;
    }

    public override bool TryReplaceDescendant(EmbedItem replacement)
    {
        if (NameItem?.Id is not null && NameItem.Id == replacement.Id && replacement is EmbedTextItem text)
        {
            NameItem = text;
            return true;
        }

        if (replacement is EmbedProgressBarItem bar)
        {
            for (var i = 0; i < Bars.Count; i++)
            {
                if (Bars[i].Id is not null && Bars[i].Id == bar.Id)
                {
                    Bars[i] = bar;
                    return true;
                }
            }
        }

        return false;
    }
}

/// <summary>
/// A single bar within a progress track.
/// </summary>
public class EmbedProgressBarItem : EmbedItem
{
    private int _value;

    /// <summary>
    /// Fill percentage, clamped to 0-100.
    /// </summary>
    public int Value
    {
        get => _value;
        set => _value = Math.Clamp(value, 0, 100);
    }

    /// <summary>
    /// When true, the percentage is rendered as a label inside the bar.
    /// </summary>
    public bool ShowLabel { get; set; }

    public bool IsStriped { get; set; }

    /// <summary>
    /// Animates the stripes. Implies <see cref="IsStriped"/> when rendered.
    /// </summary>
    public bool IsAnimated { get; set; }

    [JsonIgnore]
    public override EmbedItemType ItemType => EmbedItemType.ProgressBar;
}
