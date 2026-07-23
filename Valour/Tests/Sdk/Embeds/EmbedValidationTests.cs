using Valour.Sdk.Models.Embeds;
using Valour.Sdk.Models.Embeds.Items;

namespace Valour.Tests.Sdk.Embeds;

public class EmbedValidationTests
{
    [Fact]
    public void Validate_AcceptsWellFormedEmbed()
    {
        var embed = new EmbedBuilder()
            .AddPage("ok")
                .AddText("hello").WithId("t1")
            .Build();

        Assert.True(EmbedParser.Validate(embed).Success);
    }

    [Fact]
    public void Validate_RejectsEmptyPages()
    {
        Assert.False(EmbedParser.Validate(new Embed()).Success);
    }

    [Fact]
    public void Validate_RejectsDuplicateIds()
    {
        var embed = new Embed
        {
            Pages =
            {
                new EmbedPage
                {
                    Children =
                    {
                        new EmbedTextItem("a") { Id = "dupe" },
                        new EmbedTextItem("b") { Id = "dupe" },
                    },
                },
            },
        };

        var result = EmbedParser.Validate(embed);
        Assert.False(result.Success);
        Assert.Contains("dupe", result.Message);
    }

    [Theory]
    [InlineData("width: 50%;")]
    [InlineData("width: 50%; color: rgba(1, 2, 3, 1); justify-content: space-between;")]
    [InlineData("  margin-left: 4px ;")]
    [InlineData(null)]
    [InlineData("")]
    public void ValidateCss_AcceptsWhitelistedDeclarations(string style)
    {
        Assert.True(EmbedParser.ValidateCss(style).Success);
    }

    [Theory]
    [InlineData("background-image: url(https://evil.example/pixel.png);")] // resource loading
    [InlineData("width: expression(alert(1));")]                            // legacy script vector
    [InlineData("color: red; @import 'x';")]                                // at-rules
    [InlineData("font-family: monospace;")]                                 // not whitelisted
    [InlineData("behavior: something;")]                                    // not whitelisted
    [InlineData("just some text")]                                          // malformed
    public void ValidateCss_RejectsUnsafeOrUnknownDeclarations(string style)
    {
        Assert.False(EmbedParser.ValidateCss(style).Success);
    }

    [Fact]
    public void Validate_ChecksItemStyles()
    {
        var embed = new Embed
        {
            Pages =
            {
                new EmbedPage
                {
                    Children =
                    {
                        new EmbedTextItem("a") { Style = "background-image: url(https://evil.example);" },
                    },
                },
            },
        };

        Assert.False(EmbedParser.Validate(embed).Success);
    }

    [Fact]
    public void Validate_ChecksPageTitleStyles()
    {
        var embed = new Embed
        {
            Pages =
            {
                new EmbedPage
                {
                    TitleStyle = "width: url(bad);",
                    Children = { new EmbedTextItem("a") },
                },
            },
        };

        Assert.False(EmbedParser.Validate(embed).Success);
    }

    [Fact]
    public void Validate_RejectsInvalidClasses()
    {
        var embed = new Embed
        {
            Pages =
            {
                new EmbedPage
                {
                    Children =
                    {
                        new EmbedTextItem("a") { Classes = "ok-class bad\"onload=\"x" },
                    },
                },
            },
        };

        Assert.False(EmbedParser.Validate(embed).Success);
    }

    [Fact]
    public void ValidateItems_ChecksNestedItems()
    {
        var items = new List<EmbedItem>
        {
            new EmbedRowItem
            {
                Id = "row",
                Children =
                {
                    new EmbedTextItem("bad") { Style = "width: expression(x);" },
                },
            },
        };

        Assert.False(EmbedParser.ValidateItems(items).Success);
    }
}
