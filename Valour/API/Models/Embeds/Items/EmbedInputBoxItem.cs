using System.Text.Json.Serialization;
using Valour.Api.Items.Messages.Embeds.Styles;

namespace Valour.Api.Items.Messages.Embeds.Items;

public class EmbedInputBoxItem : EmbedItem, IEmbedFormItem, INameable
{
    /// <summary>
    /// The input value
    /// </summary>
    public string Value { get; set; }

    /// <summary>
    /// The placeholder text for inputs
    /// </summary>
    public string Placeholder { get; set; }

    public string Id { get; set; }

    public EmbedTextItem NameItem { get; set; }

	public bool? KeepValueOnUpdate { get; set; } = true;

	[JsonIgnore]
	public override EmbedItemType ItemType => EmbedItemType.InputBox;

	public EmbedInputBoxItem()
    {
        
    }

    public EmbedInputBoxItem(string id, string value = null, string placeholder = null, bool? keepvalueonupdate = null)
    {
        Id = id;
        Value = value;
        Placeholder = placeholder; 
        KeepValueOnUpdate = keepvalueonupdate;
    }
}