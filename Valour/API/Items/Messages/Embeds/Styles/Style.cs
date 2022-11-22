using System.Text.Json.Serialization;
using Valour.Api.Items.Messages.Embeds.Items;
using Valour.Api.Items.Messages.Embeds.Styles.Basic;
using Valour.Api.Items.Messages.Embeds.Styles.Flex;

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

[JsonDerivedType(typeof(BackgroundColor), typeDiscriminator: 1)]
[JsonDerivedType(typeof(BorderRadius), typeDiscriminator: 2)]
[JsonDerivedType(typeof(Border), typeDiscriminator: 3)]
[JsonDerivedType(typeof(Borders), typeDiscriminator: 4)]
[JsonDerivedType(typeof(Display), typeDiscriminator: 5)]
[JsonDerivedType(typeof(FontSize), typeDiscriminator: 6)]
[JsonDerivedType(typeof(Height), typeDiscriminator: 7)]
[JsonDerivedType(typeof(Margin), typeDiscriminator: 8)]
[JsonDerivedType(typeof(Padding), typeDiscriminator: 9)]
[JsonDerivedType(typeof(Position), typeDiscriminator: 10)]
[JsonDerivedType(typeof(TextColor), typeDiscriminator: 11)]
[JsonDerivedType(typeof(Width), typeDiscriminator: 12)]
[JsonDerivedType(typeof(Color), typeDiscriminator: 13)]
[JsonDerivedType(typeof(Size), typeDiscriminator: 14)]
[JsonDerivedType(typeof(FlexAlignContent), typeDiscriminator: 15)]
[JsonDerivedType(typeof(FlexAlignItems), typeDiscriminator: 16)]
[JsonDerivedType(typeof(FlexAlignSelf), typeDiscriminator: 17)]
[JsonDerivedType(typeof(FlexDirection), typeDiscriminator: 18)]
[JsonDerivedType(typeof(FlexGap), typeDiscriminator: 19)]
[JsonDerivedType(typeof(FlexGrow), typeDiscriminator: 20)]
[JsonDerivedType(typeof(FlexJustifyContent), typeDiscriminator: 21)]
[JsonDerivedType(typeof(FlexOrder), typeDiscriminator: 22)]
[JsonDerivedType(typeof(FlexShrink), typeDiscriminator: 23)]
[JsonDerivedType(typeof(FlexWrap), typeDiscriminator: 24)]
[JsonDerivedType(typeof(FlexJustifyContent), typeDiscriminator: 25)]
public abstract class StyleBase
{
}