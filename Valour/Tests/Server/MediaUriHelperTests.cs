using Valour.Server.Cdn;

namespace Valour.Tests.Server;

[Collection("ApiCollection")]
public class MediaUriHelperTests
{
    [Theory]
    [InlineData("https://localhost:5201/content/test.png", "localhost:5201", true)]
    [InlineData("https://localhost:5202/content/test.png", "localhost:5201", false)]
    [InlineData("https://localhost/content/test.png", "localhost:5201", false)]
    [InlineData("http://localhost:5201/content/test.png", "localhost:5201", false)]
    [InlineData("https://cdn.example.test:443/content/test.png", "CDN.example.test", true)]
    [InlineData("https://cdn.example.test.evil.test/content/test.png", "cdn.example.test", false)]
    [InlineData("https://cdn.example.test@evil.test/content/test.png", "cdn.example.test", false)]
    [InlineData("https://user@cdn.example.test/content/test.png", "cdn.example.test", false)]
    [InlineData("https://[::1]:5201/content/test.png", "[::1]:5201", true)]
    public void ConfiguredOrigins_RequireMatchingHttpsHostAndPort(string location, string host, bool allowed)
    {
        Assert.Equal(allowed, MediaUriHelper.MatchesConfiguredOrigin(new Uri(location), host));
    }
    [Fact]
    public void BucketIdentity_PreservesConfiguredPortAndRejectsOtherOrigins()
    {
        var previous = Valour.Shared.Hosting.ValourHosts.ContentCdnHost;
        try
        {
            Valour.Shared.Hosting.ValourHosts.ContentCdnHost = "localhost:5201";
            Assert.Equal("Image/42/hash.png", Valour.Server.Services.MessageService.TryParseCdnBucketItemId(
                "https://localhost:5201/content/Image/42/hash.png"));
            Assert.Null(Valour.Server.Services.MessageService.TryParseCdnBucketItemId(
                "https://localhost:5202/content/Image/42/hash.png"));
        }
        finally
        {
            Valour.Shared.Hosting.ValourHosts.ContentCdnHost = previous;
        }
    }
}
