using Valour.Sdk.Models;
using Valour.Shared.Hosting;
using Valour.Shared.Models;

namespace Valour.Tests.Sdk.Models;

public class MessageAttachmentSignedUrlTests
{
    private static ValueTask<string?> SignedUrlOf(string location) =>
        new MessageAttachment(MessageAttachmentType.Image) { Location = location }.GetSignedUrl(null!, null!);

    [Fact]
    public async Task ProxiedMedia_StaysAbsoluteOnTheCdn()
    {
        var location = $"{ValourHosts.ContentCdnBaseUrl}/proxy/ea2baae3c938386c.png";

        Assert.Equal(location, await SignedUrlOf(location));
    }

    [Fact]
    public async Task ProxiedMedia_OnAnotherHost_IsLoadedFromTheCdn()
    {
        Assert.Equal($"{ValourHosts.ContentCdnBaseUrl}/proxy/abc.png",
            await SignedUrlOf("https://attacker.example/proxy/abc.png"));
    }
}
