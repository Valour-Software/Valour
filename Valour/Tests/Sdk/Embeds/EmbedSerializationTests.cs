using Valour.Sdk.Models;
using Valour.Sdk.Models.Embeds;
using Valour.Sdk.Models.Embeds.Items;
using Valour.Shared.Models;

namespace Valour.Tests.Sdk.Embeds;

public class EmbedSerializationTests
{
    private static Embed BuildFullEmbed()
    {
        return new EmbedBuilder()
            .WithEmbedId("test-embed")
            .WithEmbedName("Test")
            .WithRevision(3)
            .AddPage("Page One", "footer text")
                .AddText("hello world").WithId("text-1")
                .AddRow()
                    .AddButton("Click me").WithId("button-1").OnClickEvent("clicked")
                    .AddButton("Docs").OnClickLink("https://valour.gg")
                    .AddButton("Next").OnClickPage(1)
                .EndRow()
                .AddForm("form-1")
                    .AddInputBox("name-input", name: "Your Name", placeholder: "name...")
                    .AddDropDown("color-pick", name: "Color", value: "Red")
                        .AddOption("Red")
                        .AddOption("Blue", "blue-value")
                    .AddButton("Submit").OnClickSubmitForm("submitted")
                .EndForm()
                .AddProgress("Loading")
                    .AddProgressBar(40).WithId("bar-1").WithLabel().Striped()
                .AddMedia(MessageAttachmentType.Image, 100, 50, "image/png", "img.png", "https://cdn.valour.gg/img.png")
            .AddPage("Page Two")
                .AddText("named", "value").WithId("text-2")
            .Build();
    }

    [Fact]
    public void RoundTrip_PreservesTree()
    {
        var embed = BuildFullEmbed();
        var json = EmbedParser.Serialize(embed);
        var parsed = EmbedParser.TryParse(json);

        Assert.NotNull(parsed);
        Assert.Equal(embed.Id, parsed.Id);
        Assert.Equal(embed.Name, parsed.Name);
        Assert.Equal(3, parsed.Revision);
        Assert.Equal(2, parsed.Pages.Count);
        Assert.Equal("Page One", parsed.Pages[0].Title);
        Assert.Equal("footer text", parsed.Pages[0].Footer);

        // Same item count, types, and ids after round trip
        var originalItems = embed.EnumerateItems().ToList();
        var parsedItems = parsed.EnumerateItems().ToList();
        Assert.Equal(originalItems.Count, parsedItems.Count);
        for (var i = 0; i < originalItems.Count; i++)
        {
            Assert.Equal(originalItems[i].GetType(), parsedItems[i].GetType());
            Assert.Equal(originalItems[i].Id, parsedItems[i].Id);
        }
    }

    [Fact]
    public void RoundTrip_PreservesClickTargets()
    {
        var json = EmbedParser.Serialize(BuildFullEmbed());
        var parsed = EmbedParser.TryParse(json)!;

        var button = Assert.IsType<EmbedButtonItem>(parsed.FindItem("button-1"));
        var eventTarget = Assert.IsType<EmbedEventTarget>(button.ClickTarget);
        Assert.Equal("clicked", eventTarget.EventId);

        var buttons = parsed.EnumerateItems().OfType<EmbedButtonItem>().ToList();
        Assert.Contains(buttons, b => b.ClickTarget is EmbedLinkTarget { Href: "https://valour.gg" });
        Assert.Contains(buttons, b => b.ClickTarget is EmbedPageTarget { PageIndex: 1 });
        Assert.Contains(buttons, b => b.ClickTarget is EmbedFormSubmitTarget { EventId: "submitted" });
    }

    [Fact]
    public void Serialization_UsesStableStringDiscriminators()
    {
        // Guards against accidental wire-format breaks: bots depend on these
        var json = EmbedParser.Serialize(BuildFullEmbed());

        Assert.Contains("\"$type\":\"text\"", json);
        Assert.Contains("\"$type\":\"button\"", json);
        Assert.Contains("\"$type\":\"row\"", json);
        Assert.Contains("\"$type\":\"form\"", json);
        Assert.Contains("\"$type\":\"input\"", json);
        Assert.Contains("\"$type\":\"dropdown\"", json);
        Assert.Contains("\"$type\":\"progress\"", json);
        Assert.Contains("\"$type\":\"media\"", json);
        Assert.Contains("\"$type\":\"link\"", json);
        Assert.Contains("\"$type\":\"page\"", json);
        Assert.Contains("\"$type\":\"event\"", json);
        Assert.Contains("\"$type\":\"submit\"", json);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{\"Pages\": \"wrong shape\"}")]
    public void TryParse_ReturnsNull_ForInvalidPayloads(string payload)
    {
        Assert.Null(EmbedParser.TryParse(payload));
    }

    [Fact]
    public void TryParse_ReturnsNull_ForLegacyV1Payload()
    {
        // v1 embeds serialized an EmbedVersion string property
        const string legacy = "{\"Pages\":[],\"EmbedVersion\":\"1.3\"}";
        Assert.Null(EmbedParser.TryParse(legacy));
    }

    [Fact]
    public void TryParse_ReturnsNull_ForUnknownItemDiscriminator()
    {
        const string payload = "{\"Version\":2,\"Pages\":[{\"Children\":[{\"$type\":\"hologram\"}]}]}";
        Assert.Null(EmbedParser.TryParse(payload));
    }

    [Fact]
    public void TryParse_ReturnsNull_ForWrongVersion()
    {
        const string payload = "{\"Version\":3,\"Pages\":[{\"Children\":[]}]}";
        Assert.Null(EmbedParser.TryParse(payload));
    }

    [Fact]
    public void TryParse_ReturnsNull_ForEmptyPages()
    {
        const string payload = "{\"Version\":2,\"Pages\":[]}";
        Assert.Null(EmbedParser.TryParse(payload));
    }

    [Fact]
    public void TryParse_ReturnsNull_ForOversizedPayload()
    {
        var payload = "{\"Version\":2,\"Pages\":[]}" + new string(' ', EmbedParser.MaxPayloadLength);
        Assert.Null(EmbedParser.TryParse(payload));
    }

    [Fact]
    public void TryParseItems_RoundTripsPolymorphicList()
    {
        var items = new List<EmbedItem>
        {
            new EmbedTextItem("updated") { Id = "text-1" },
            new EmbedProgressBarItem { Id = "bar-1", Value = 80 },
        };

        var json = System.Text.Json.JsonSerializer.Serialize(items);
        var parsed = EmbedParser.TryParseItems(json);

        Assert.NotNull(parsed);
        Assert.Equal(2, parsed.Count);
        Assert.IsType<EmbedTextItem>(parsed[0]);
        Assert.IsType<EmbedProgressBarItem>(parsed[1]);
    }

    [Fact]
    public void TryParseItems_ReturnsNull_ForMalformedJson()
    {
        Assert.Null(EmbedParser.TryParseItems("[{ not json"));
    }
}
