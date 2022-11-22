using System.Text.Json.Serialization;
using Valour.Api.Items.Messages.Embeds.Items;

namespace Valour.Api.Items.Messages.Embeds.Styles;

public enum EmbedStyleType
{
    BackgroundColor = 0,
    BorderRadius = 1,
    Border = 2,
    Borders = 3,
    Display = 4,
    FontSize = 5,
    Height = 6,
    Margin = 7,
    Padding = 8,
    Position = 9,
    TextColor = 10,
    Width = 11,
    Color = 12,
    Size = 13,
    FlexAlignContent = 14,
    FlexAlignItems = 15,
    FlexAlignSelf = 16,
    FlexDirection = 17,
    FlexGap = 18,
    FLexGrow = 19,
    FlexJustifyContent = 20,
    FlexOrder = 21,
    FlexShrink = 22,
    FlexWrap = 23
}

[JsonDerivedType(typeof(EmbedItem), typeDiscriminator: 1)]
[JsonDerivedType(typeof(EmbedTextItem), typeDiscriminator: 2)]
[JsonDerivedType(typeof(EmbedButtonItem), typeDiscriminator: 3)]
[JsonDerivedType(typeof(EmbedFormItem), typeDiscriminator: 4)]
[JsonDerivedType(typeof(EmbedInputBoxItem), typeDiscriminator: 5)]
[JsonDerivedType(typeof(EmbedDropDownMenuItem), typeDiscriminator: 6)]
[JsonDerivedType(typeof(EmbedDropDownItem), typeDiscriminator: 7)]
public abstract class IStyle
{
    [JsonPropertyName("t")]
    public EmbedStyleType Type { get; set; }
}