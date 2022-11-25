using Valour.Api.Items.Messages.Embeds;
using System;
using System.Text.Json.Serialization;
using Valour.Api.Items.Messages.Embeds.Styles.Flex;
using Valour.Api.Items.Messages.Embeds.Styles;

namespace Valour.Api.Items.Messages.Embeds.Items;

public enum EmbedItemType
{
    Text = 1,
    Button = 2,
    InputBox = 3,
    TextArea = 4,
    ProgressBar = 5,
    Form = 6,
    GoTo = 7,
    DropDownItem = 8,
    DropDownMenu = 9,
    EmbedRow = 10,
    EmbedPage = 11
}

public interface IEmbedFormItem
{
    public string Id { get; set; }
    public string Value { get; set; }
    public EmbedItemType ItemType { get; }
}

public interface IClickable
{
    [JsonPropertyName("ct")]
    public EmbedClickTargetBase ClickTarget { get; set; }
}
public interface INameable
{
    public EmbedTextItem NameItem { get; set; }
}

public interface IParentItem
{
	public List<EmbedItem> Children { get; set; }

	public EmbedItemType ItemType { get; }

	public IParentItem Parent { get; set; }
}

[JsonDerivedType(typeof(EmbedItem), typeDiscriminator: 1)]
[JsonDerivedType(typeof(EmbedTextItem), typeDiscriminator: 2)]
[JsonDerivedType(typeof(EmbedButtonItem), typeDiscriminator: 3)]
[JsonDerivedType(typeof(EmbedFormItem), typeDiscriminator: 4)]
[JsonDerivedType(typeof(EmbedInputBoxItem), typeDiscriminator: 5)]
[JsonDerivedType(typeof(EmbedDropDownMenuItem), typeDiscriminator: 6)]
[JsonDerivedType(typeof(EmbedDropDownItem), typeDiscriminator: 7)]
[JsonDerivedType(typeof(EmbedRow), typeDiscriminator: 8)]
public class EmbedItem : IParentItem
{
    [JsonIgnore]
    public virtual EmbedItemType ItemType { get; }
    
    public List<EmbedItem> Children { get; set; }

    [JsonIgnore]
	public IParentItem Parent { get; set; }

	public List<StyleBase> Styles { get; set; }

	[JsonIgnore]
    public Embed Embed { get; set; }

	public virtual EmbedItem GetLastItem(bool InsideofForms)
	{
        if (Children.Count == 0)
            return this;
        else
        {
            return Children.Last().GetLastItem(InsideofForms);
        }
	}

    public void Init(Embed embed, IParentItem parent)
    {
        Parent = parent;
        Embed = embed;
        if (Children is not null) {
            foreach(var item in Children) 
            {
                item.Init(embed, this);
            }
        }
        if (Children is null)
        {
            Children = new();
		}
    }

	public string GetStyle()
    {
        string style = "";
        if (Styles is not null)
        {
            foreach(var _style in Styles)
            {
                style += _style;
            }
        }

        if (ItemType == EmbedItemType.EmbedRow && !style.Contains("display: flex"))
            style += "display: flex;";

        else if (Parent is null)
            return style;

        if (Parent.ItemType == EmbedItemType.EmbedRow && !(style.Contains("margin-right") || style.Contains("margin-left")))
            style += "margin-right: 5px;";

		return style;
    }
}
