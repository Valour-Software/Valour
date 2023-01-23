using System.Text.Json.Serialization;
using Valour.Api.Items.Messages.Embeds.Items;
using Valour.Api.Items.Messages.Embeds.Styles.Basic;
using Valour.Api.Items.Messages.Embeds.Styles.Flex;

namespace Valour.Api.Items.Messages.Embeds.Styles;

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
[JsonDerivedType(typeof(TextDecoration), typeDiscriminator: 25)]
[JsonDerivedType(typeof(FontWeight), typeDiscriminator: 26)]
public abstract class StyleBase
{
	public StyleBase() { }
}