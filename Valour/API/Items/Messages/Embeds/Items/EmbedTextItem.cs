using System.Text.Json.Serialization;
using Valour.Api.Items.Messages.Embeds.Styles;

namespace Valour.Api.Items.Messages.Embeds.Items;

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