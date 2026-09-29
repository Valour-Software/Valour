using Microsoft.AspNetCore.Components;
using Valour.Client.Utility;

namespace Valour.Tests.Client;

public class ShareUtilsTests
{
    [Theory]
    // The official app shares links on the shorter root domain, which
    // redirects them into the app.
    [InlineData("https://app.valour.gg/", "https://valour.gg/i/test-code")]
    [InlineData("http://0.0.0.1/", "https://valour.gg/i/test-code")]
    [InlineData("http://0.0.0.0:5000/", "https://valour.gg/i/test-code")]
    [InlineData("https://self-host.example/app/", "https://self-host.example/app/i/test-code")]
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
