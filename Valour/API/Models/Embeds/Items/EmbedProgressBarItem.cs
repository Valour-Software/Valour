using System.Text.Json.Serialization;
using Valour.Api.Items.Messages.Embeds.Styles;
using Valour.Api.Items.Messages.Embeds.Styles.Bootstrap;

namespace Valour.Api.Items.Messages.Embeds.Items;

public class EmbedProgressBar : EmbedItem
{
    public int Value { get; set; }

    public bool ShowLabel { get; set; }
    public bool IsStriped { get; set; } = false;
    public bool IsAnimatedStriped { get; set; } = false;

	[JsonIgnore]
	public override EmbedItemType ItemType => EmbedItemType.ProgressBar;

    public override string GetStyle()
    {
        return $"width: {Value}%;"+base.GetStyle();
    }

    public override string GetClasses()
    {
        string classes = base.GetClasses()+"progress-bar";
        if (IsStriped)
            classes += " progress-bar-striped";
        if (IsAnimatedStriped)
            classes += " progress-bar-animated";
        return classes;
    }
}