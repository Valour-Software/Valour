using Valour.Api.Items.Messages.Embeds;
using System;
using System.Text.Json.Serialization;

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
    DropDownMenu = 9
}

public interface IEmbedFormItem
{
    public string Id { get; set; }
    public string Value { get; set; }
    public EmbedItemType ItemType { get; set; }
}

[JsonDerivedType(typeof(EmbedItem), typeDiscriminator: 1)]
[JsonDerivedType(typeof(EmbedTextItem), typeDiscriminator: 2)]
[JsonDerivedType(typeof(EmbedButtonItem), typeDiscriminator: 3)]
[JsonDerivedType(typeof(EmbedFormItem), typeDiscriminator: 4)]
[JsonDerivedType(typeof(EmbedInputBoxItem), typeDiscriminator: 5)]
[JsonDerivedType(typeof(EmbedDropDownMenuItem), typeDiscriminator: 6)]
[JsonDerivedType(typeof(EmbedDropDownItem), typeDiscriminator: 7)]
public class EmbedItem
{
    public EmbedItemType ItemType { get; set; }

    /// <summary>
    /// Should be null if the embed is not FreelyBased
    /// </summary>
    public int? X { get; set; }

    /// <summary>
    /// Should be null if the embed is not FreelyBased
    /// </summary>
    public int? Y { get; set; }

    public string? Href { get; set; }

    public int? Page { get; set; }

    /// <summary>
	/// If not null, what will the event name be for when a user clicks on this text item
	/// </summary>
    public string? OnClickEventName { get; set; }

    [JsonIgnore]
    public Embed Embed { get; set; }

    public bool HasGoTo
    {
        get
        {
            return Href is not null || Page is not null;
        }
    }

    public string GetStyle()
    {
        string style = "";
        if (X is not null)
        {
            int x = Math.Min((int)X, 1024);
            int y = Math.Min((int)Y, 600);
            if (x < 0)
                x = 0;
            if (y < 0)
                y = 0;
            if (Embed.CurrentlyDisplayed.Title is not null)
                y += 42;
            style += $"position: absolute;left: calc(1rem + {x+59}px);top: calc(1rem + {y}px);width: fit-content";
        }
        else
        {
            style += "display: inline-block;margin-right:5px;";
        }

        return style;
    }
}
