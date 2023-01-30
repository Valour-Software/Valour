using System.Text.Json.Serialization;
using Valour.Api.Models.Messages.Embeds.Styles;

namespace Valour.Api.Models.Messages.Embeds.Items;

public class EmbedTextItem : EmbedItem, IClickable, INameable
{
    public EmbedTextItem NameItem { get; set; }
    public string Text { get; set; }
	public EmbedClickTargetBase ClickTarget { get; set; }

	[JsonIgnore]
	public override EmbedItemType ItemType => EmbedItemType.Text;

	public EmbedTextItem()
	{

	}

	public EmbedTextItem(string text)
	{
		Text = text;
	}
}