using System.Text;

namespace Valour.Sdk.Models.Embeds.Styles;

public enum DisplayType { None, Block, Inline, InlineBlock, Flex, InlineFlex, Grid }
public enum PositionType { Static, Relative, Absolute, Fixed, Sticky }
public enum TextDecorationType { None, Underline, Overline, LineThrough }
public enum FontWeightType { Normal, Bold, Bolder, Lighter }
public enum FlexDirectionType { Row, RowReverse, Column, ColumnReverse }
public enum FlexWrapType { Nowrap, Wrap, WrapReverse }
public enum JustifyContentType { FlexStart, FlexEnd, Center, SpaceBetween, SpaceAround, SpaceEvenly }
public enum AlignItemsType { Stretch, FlexStart, FlexEnd, Center, Baseline }
public enum AlignContentType { Stretch, FlexStart, FlexEnd, Center, SpaceBetween, SpaceAround }
public enum AlignSelfType { Auto, Stretch, FlexStart, FlexEnd, Center, Baseline }
public enum TextAlignType { Left, Right, Center, Justify }

/// <summary>
/// Typed factories for embed CSS declarations. These compile to plain CSS
/// at build time; the server validates the compiled declarations against
/// a property whitelist, so prefer these over <see cref="Raw"/>.
/// </summary>
public static class EmbedStyles
{
    // Sizing
    public static StyleValue Width(Size size) => new("width", size.ToString());
    public static StyleValue MinWidth(Size size) => new("min-width", size.ToString());
    public static StyleValue MaxWidth(Size size) => new("max-width", size.ToString());
    public static StyleValue Height(Size size) => new("height", size.ToString());
    public static StyleValue MinHeight(Size size) => new("min-height", size.ToString());
    public static StyleValue MaxHeight(Size size) => new("max-height", size.ToString());

    // Color and text
    public static StyleValue BackgroundColor(Color color) => new("background-color", color.ToString());
    public static StyleValue TextColor(Color color) => new("color", color.ToString());
    public static StyleValue FontSize(Size size) => new("font-size", size.ToString());
    public static StyleValue FontWeight(FontWeightType weight) => new("font-weight", ToCss(weight));
    public static StyleValue FontWeight(int weight) => new("font-weight", weight.ToString());
    public static StyleValue TextDecoration(TextDecorationType decoration) => new("text-decoration", ToCss(decoration));
    public static StyleValue TextAlign(TextAlignType align) => new("text-align", ToCss(align));

    // Box
    public static StyleValue Margin(Size size) => new("margin", size.ToString());
    public static StyleValue MarginLeft(Size size) => new("margin-left", size.ToString());
    public static StyleValue MarginRight(Size size) => new("margin-right", size.ToString());
    public static StyleValue MarginTop(Size size) => new("margin-top", size.ToString());
    public static StyleValue MarginBottom(Size size) => new("margin-bottom", size.ToString());
    public static StyleValue Padding(Size size) => new("padding", size.ToString());
    public static StyleValue PaddingLeft(Size size) => new("padding-left", size.ToString());
    public static StyleValue PaddingRight(Size size) => new("padding-right", size.ToString());
    public static StyleValue PaddingTop(Size size) => new("padding-top", size.ToString());
    public static StyleValue PaddingBottom(Size size) => new("padding-bottom", size.ToString());
    public static StyleValue Border(Size width, Color color) => new("border", $"{width} solid {color}");
    public static StyleValue BorderRadius(Size size) => new("border-radius", size.ToString());

    // Layout
    public static StyleValue Display(DisplayType display) => new("display", ToCss(display));
    public static StyleValue Position(PositionType position) => new("position", ToCss(position));
    public static StyleValue Top(Size size) => new("top", size.ToString());
    public static StyleValue Right(Size size) => new("right", size.ToString());
    public static StyleValue Bottom(Size size) => new("bottom", size.ToString());
    public static StyleValue Left(Size size) => new("left", size.ToString());

    // Flex
    public static StyleValue FlexDirection(FlexDirectionType direction) => new("flex-direction", ToCss(direction));
    public static StyleValue FlexWrap(FlexWrapType wrap) => new("flex-wrap", ToCss(wrap));
    public static StyleValue JustifyContent(JustifyContentType justify) => new("justify-content", ToCss(justify));
    public static StyleValue AlignItems(AlignItemsType align) => new("align-items", ToCss(align));
    public static StyleValue AlignContent(AlignContentType align) => new("align-content", ToCss(align));
    public static StyleValue AlignSelf(AlignSelfType align) => new("align-self", ToCss(align));
    public static StyleValue FlexGrow(int grow) => new("flex-grow", grow.ToString());
    public static StyleValue FlexShrink(int shrink) => new("flex-shrink", shrink.ToString());
    public static StyleValue Order(int order) => new("order", order.ToString());
    public static StyleValue Gap(Size size) => new("gap", size.ToString());

    /// <summary>
    /// Escape hatch for properties without a typed factory. The property must
    /// still pass the server-side whitelist to be accepted.
    /// </summary>
    public static StyleValue Raw(string property, string value) => new(property, value);

    /// <summary>
    /// Converts an enum member name to its CSS keyword (SpaceBetween → space-between).
    /// </summary>
    internal static string ToCss<T>(T value) where T : struct, Enum
    {
        var name = value.ToString();
        var sb = new StringBuilder(name.Length + 4);

        for (var i = 0; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]))
            {
                if (i > 0)
                    sb.Append('-');
                sb.Append(char.ToLowerInvariant(name[i]));
            }
            else
            {
                sb.Append(name[i]);
            }
        }

        return sb.ToString();
    }
}
