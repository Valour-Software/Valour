using Valour.Sdk.Models.Embeds;
using Valour.Sdk.Models.Embeds.Styles;

namespace Valour.Tests.Sdk.Embeds;

public class EmbedStyleTests
{
    [Fact]
    public void StyleValue_CompilesDeclarations()
    {
        Assert.Equal("width: 50%;", EmbedStyles.Width(Size.Half).ToString());
        Assert.Equal("height: 24px;", EmbedStyles.Height(Size.Pixels(24)).ToString());
        Assert.Equal("font-size: 1.5em;", EmbedStyles.FontSize(Size.Em(1.5)).ToString());
        Assert.Equal("width: auto;", EmbedStyles.Width(Size.Auto).ToString());
        Assert.Equal("margin: 0;", EmbedStyles.Margin(Size.Zero).ToString());
    }

    [Fact]
    public void EnumKeywords_ConvertToKebabCase()
    {
        Assert.Equal("justify-content: space-between;", EmbedStyles.JustifyContent(JustifyContentType.SpaceBetween).ToString());
        Assert.Equal("display: inline-flex;", EmbedStyles.Display(DisplayType.InlineFlex).ToString());
        Assert.Equal("flex-direction: row-reverse;", EmbedStyles.FlexDirection(FlexDirectionType.RowReverse).ToString());
        Assert.Equal("font-weight: bold;", EmbedStyles.FontWeight(FontWeightType.Bold).ToString());
        Assert.Equal("text-decoration: line-through;", EmbedStyles.TextDecoration(TextDecorationType.LineThrough).ToString());
    }

    [Fact]
    public void Color_ParsesHex()
    {
        var color = new Color("#FF8000");
        Assert.Equal("rgba(255, 128, 0, 1)", color.ToString());

        var noHash = new Color("ff8000");
        Assert.Equal(color.ToString(), noHash.ToString());
    }

    [Fact]
    public void Color_ParsesHexWithAlpha()
    {
        var color = new Color("#00000080");
        Assert.Equal(0.5f, color.Alpha, precision: 2);
    }

    [Theory]
    [InlineData("xyz")]
    [InlineData("#ff")]
    [InlineData("")]
    public void Color_RejectsInvalidHex(string hex)
    {
        Assert.ThrowsAny<Exception>(() => new Color(hex));
    }

    [Fact]
    public void WithStyle_CompilesOntoItem()
    {
        var embed = new EmbedBuilder()
            .AddPage()
                .AddText("styled")
                    .WithStyle(
                        EmbedStyles.Width(Size.Half),
                        EmbedStyles.TextColor(new Color(255, 0, 0)))
                    .WithStyle(EmbedStyles.FontWeight(FontWeightType.Bold))
            .Build();

        var item = embed.Pages[0].Children[0];
        Assert.Equal("width: 50%; color: rgba(255, 0, 0, 1); font-weight: bold;", item.Style);
    }

    [Fact]
    public void WithClasses_Appends()
    {
        var embed = new EmbedBuilder()
            .AddPage()
                .AddText("x").WithClasses("bg-primary", "rounded").WithClasses("extra")
            .Build();

        Assert.Equal("bg-primary rounded extra", embed.Pages[0].Children[0].Classes);
    }
}
