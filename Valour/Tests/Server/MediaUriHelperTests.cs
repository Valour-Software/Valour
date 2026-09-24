using Valour.Sdk.Models;
using Valour.Server.Cdn;
using Valour.Shared.Models;

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
    [Theory]
    // Script and foreign hosts are rejected even when marked inline, since
    // embed JSON can carry a client-supplied Inline flag.
    [InlineData(MessageAttachmentType.YouTube, "javascript:alert(1)", true, false)]
    [InlineData(MessageAttachmentType.Twitch, "javascript:alert(1)//", true, false)]
    [InlineData(MessageAttachmentType.Spotify, "https://evil.example/embed", true, false)]
    [InlineData(MessageAttachmentType.Bluesky, "data:text/html,x", false, false)]
    [InlineData(MessageAttachmentType.YouTube, "https://www.youtube.com/embed/abc", false, true)]
    [InlineData(MessageAttachmentType.Twitch, "https://player.twitch.tv/?channel=x&parent=valour.gg", false, true)]
    // Server-built inline previews of plain http provider links still pass.
    [InlineData(MessageAttachmentType.Twitter, "http://twitter.com/user/status/1", true, true)]
    [InlineData(MessageAttachmentType.Twitter, "http://twitter.com/user/status/1", false, false)]
    public void ScanMediaUri_RequiresWebSchemeAndProviderHost(
        MessageAttachmentType type, string location, bool inline, bool allowed)
    {
        var attachment = new MessageAttachment(type) { Location = location, Inline = inline };

        Assert.Equal(allowed, MediaUriHelper.ScanMediaUri(attachment).Success);
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
