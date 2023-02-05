using System.Text.Json.Serialization;
using Valour.Api.Items.Messages.Embeds.Styles;
using Valour.Api.Items.Messages.Embeds.Styles.Bootstrap;

namespace Valour.Api.Items.Messages.Embeds.Items;

public class EmbedProgress : EmbedItem, INameable
{
    public EmbedTextItem NameItem { get; set; }

	[JsonIgnore]
	public override EmbedItemType ItemType => EmbedItemType.Progress;

    public override string GetClasses()
    {
        return base.GetClasses()+"progress";
    }
}