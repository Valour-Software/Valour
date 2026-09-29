using Valour.Server.Models;
using Valour.Server.Services;
using Valour.Server.Utilities;

namespace Valour.Tests.Server;

public class LinkPreviewMetaTests
{
    private static LinkPreviewMeta Sample => new()
    {
        Title = "Tom & \"Jerry\" <script>",
        Description = "Café · 日本語",
        Url = "https://app.valour.gg/i/abc?x=1&y=2",
    };

    [Fact]
    public void RenderHead_EncodesMarkupButKeepsReadableText()
    {
        var head = Sample.RenderHead();

        Assert.DoesNotContain("<script>", head);
        Assert.Contains("Tom &amp; &quot;Jerry&quot; &lt;script&gt;", head);
        Assert.Contains("content=\"Café · 日本語\"", head);
        Assert.Contains("href=\"https://app.valour.gg/i/abc?x=1&amp;y=2\"", head);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(head, "<title>"));
    }

    [Fact]
    public void RenderHead_UsesNameAttributeForTwitterTags()
    {
        var head = (Sample with { LargeImage = true, NoIndex = true }).RenderHead();

        Assert.Contains("<meta name=\"twitter:card\" content=\"summary_large_image\">", head);
        Assert.Contains("<meta name=\"robots\" content=\"noindex\">", head);
        Assert.DoesNotContain("property=\"twitter:", head);
    }

    [Fact]
    public void ApplyToHostPage_ReplacesOnlyTheMarkedBlock()
    {
        var page = "<head><meta charset=\"utf-8\">" + LinkPreviewMeta.HostPageStartMarker +
                   "<title>Valour</title><meta property=\"og:title\" content=\"Valour\">" +
                   LinkPreviewMeta.HostPageEndMarker + "<base href=\"/\"></head>";

        var result = Sample.ApplyToHostPage(page);

        Assert.StartsWith("<head><meta charset=\"utf-8\">", result);
        Assert.EndsWith(LinkPreviewMeta.HostPageEndMarker + "<base href=\"/\"></head>", result);
        Assert.DoesNotContain("<title>Valour</title>", result);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(result, "<title>"));
    }

    [Fact]
    public void ApplyToHostPage_LeavesUnmarkedPagesAlone()
    {
        const string page = "<head><title>Valour</title></head>";
        Assert.Equal(page, Sample.ApplyToHostPage(page));
    }

    [Fact]
    public void WithPlanet_HidesTheIconOfAgeRestrictedPlanets()
    {
        var planet = new PlanetListInfo { PlanetId = 42, Name = "Night", HasCustomIcon = true, Nsfw = true };

        var meta = Sample.WithPlanet(planet);

        Assert.Equal(LinkPreviewMeta.DefaultImage, meta.Image);
        Assert.Equal(LinkPreviewMeta.DefaultImage, LinkPreviewMeta.PlanetImageUrl(planet));
        Assert.Matches("^#[0-9a-f]{6}$", meta.ThemeColor);
    }

    [Fact]
    public void ForPlanet_DescribesAgeRestrictedPlanetsWithoutTheirDescription()
    {
        var planet = new PlanetListInfo
        {
            PlanetId = 42, Name = "Night", Description = "secret", Nsfw = true, MemberCount = 1234,
        };

        var meta = AppLinkPreviewService.ForPlanet(planet, "https://app.valour.gg/i/x", "Join Night on Valour", true);

        Assert.Equal("An age-restricted community on Valour · 1,234 members", meta.Description);
        Assert.True(meta.NoIndex);
    }

    [Theory]
    [InlineData("/i/abc123", true)]
    [InlineData("/I/abc-_1/", true)]
    [InlineData("/d/12215159187308544", true)]
    [InlineData("/planet/1", true)]
    [InlineData("/i/abc/extra", false)]
    [InlineData("/i/", false)]
    [InlineData("/d/notanumber", false)]
    [InlineData("/channels/1", false)]
    [InlineData("", false)]
    public void Handles_OnlyRecognizesInviteAndPlanetPaths(string path, bool expected)
    {
        Assert.Equal(expected, AppLinkPreviewService.Handles(path));
    }
}
