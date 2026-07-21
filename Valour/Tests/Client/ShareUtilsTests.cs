using Microsoft.AspNetCore.Components;
using Valour.Client.Utility;

namespace Valour.Tests.Client;

public class ShareUtilsTests
{
    [Theory]
    [InlineData("http://0.0.0.1/", "https://app.valour.gg/I/test-code")]
    [InlineData("http://0.0.0.0:5000/", "https://app.valour.gg/I/test-code")]
    [InlineData("https://self-host.example/app/", "https://self-host.example/app/I/test-code")]
    public void GetInviteShareUrl_UsesExternallyReachableOrigin(string baseUri, string expected)
    {
        ClientHosts.AppBaseUrl = "https://app.valour.gg";
        var navigation = new TestNavigationManager(baseUri);

        var result = ShareUtils.GetInviteShareUrl(navigation, "test-code");

        Assert.Equal(expected, result);
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager(string baseUri) => Initialize(baseUri, baseUri);

        protected override void NavigateToCore(string uri, NavigationOptions options) { }
    }
}
