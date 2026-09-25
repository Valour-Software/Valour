using Valour.Server.Services;

namespace Valour.Tests.Server;

public class E2eeMessageServiceTests
{
    [Fact]
    public void ServerKeepsSpoilerMarkersOnPreviewUrls()
    {
        var urls = E2eeMessageService.SanitizePreviewUrls(
            ["||https://a.example/||", "https://b.example/", "||javascript:alert(1)||", "||https://c.example/|x||"]);
        Assert.Equal(["||https://a.example/||", "https://b.example/"], urls);
    }
}
