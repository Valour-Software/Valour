using System.Text.Json.Serialization;
using Valour.Api.Models.Messages.Embeds.Styles;
using Valour.Api.Models.Messages.Embeds.Styles.Bootstrap;

namespace Valour.Api.Models.Messages.Embeds.Items;

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