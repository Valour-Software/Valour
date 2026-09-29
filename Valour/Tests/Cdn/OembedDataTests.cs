using System.Text.Json;
using Valour.Shared.Models;

namespace Valour.Tests.Cdn;

public class OembedDataTests
{
    [Fact]
    public void Read_AcceptsPercentWidthAndNumericVersion()
    {
        // SoundCloud's oEmbed shape.
        var data = JsonSerializer.Deserialize<OembedData>(
            """{"version":1,"type":"rich","width":"100%","height":400,"html":"<iframe></iframe>"}""");

        Assert.NotNull(data);
        Assert.Equal("1", data.Version);
        Assert.Null(data.Width);
        Assert.Equal(400, data.Height);
        Assert.Equal("<iframe></iframe>", data.Html);
    }

    [Fact]
    public void Read_AcceptsNumericCacheAgeAndNullHeight()
    {
        // Bluesky's oEmbed shape.
        var data = JsonSerializer.Deserialize<OembedData>(
            """{"type":"rich","version":"1.0","cache_age":86400,"width":600,"height":null,"html":"x"}""");

        Assert.NotNull(data);
        Assert.Equal("1.0", data.Version);
        Assert.Equal(86400, data.CacheAge);
        Assert.Equal(600, data.Width);
        Assert.Null(data.Height);
    }

    [Fact]
    public void Read_AcceptsNumericStrings()
    {
        var data = JsonSerializer.Deserialize<OembedData>(
            """{"width":"550","height":"315","cache_age":"3600"}""");

        Assert.NotNull(data);
        Assert.Equal(550, data.Width);
        Assert.Equal(315, data.Height);
        Assert.Equal(3600, data.CacheAge);
    }
}
