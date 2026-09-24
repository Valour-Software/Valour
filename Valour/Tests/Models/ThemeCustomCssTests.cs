using Valour.Shared.Models.Themes;

namespace Valour.Tests.Models;

public class ThemeCustomCssTests
{
    [Theory]
    [InlineData(".app { color: red; }")]
    [InlineData(".banner { background-image: var(--theme-asset-banner); background-size: cover; }")]
    [InlineData("/* comment */ .msg:hover { border-radius: var(--radius-md); }")]
    [InlineData(".a::before { content: \"hello\"; }")]
    [InlineData("@media (max-width: 600px) { .sidebar { display: none; } }")]
    public void AllowsOrdinaryThemeCss(string css)
    {
        Assert.Null(ThemeCustomCss.GetViolation(css));
    }

    [Theory]
    [InlineData(".a { background: url(https://example.com/x.png); }")]
    [InlineData(".a { background: URL(https://example.com/x.png); }")]
    [InlineData(".a { background: u\\72l(https://example.com/x.png); }")]
    [InlineData("@\\69mport \"https://example.com/x.css\";")]
    [InlineData("@import \"https://example.com/x.css\";")]
    [InlineData("@font-face { font-family: x; }")]
    [InlineData("@namespace svg \"http://www.w3.org/2000/svg\";")]
    [InlineData(".a { background: image-set(\"https://example.com/x.png\" 1x); }")]
    [InlineData(".a { background: -webkit-image-set(\"https://example.com/x.png\" 1x); }")]
    [InlineData(".a { background: src(\"https://example.com/x.png\"); }")]
    [InlineData(".a { background: image(\"https://example.com/x.png\"); }")]
    [InlineData(".a { background: cross-fade(\"https://example.com/x.png\", red); }")]
    [InlineData(".a { background: element(#x); }")]
    [InlineData(".a { background: ur/**/l(https://example.com/x.png); }")]
    [InlineData("@im/* hidden */port \"https://example.com/x.css\";")]
    [InlineData("input[value^=\"a\"] { color: red; }")]
    [InlineData(".a { width: expression(alert(1)); }")]
    [InlineData(".a { -moz-binding: none; }")]
    [InlineData(".a { behavior: none; }")]
    [InlineData(".a { content: \"\\f101\"; }")]
    public void RejectsUnsafeThemeCss(string css)
    {
        Assert.NotNull(ThemeCustomCss.GetViolation(css));
    }
}
