using System.Net;
using Valour.Server.Cdn;

namespace Valour.Tests.Cdn;

public class SsrfSafeConnectTests
{
    /// <summary>
    /// The connect callback must reject a private/reserved destination at
    /// connect time — this is what closes the DNS-rebinding window, since the
    /// check happens on the exact address being dialed, not a prior lookup.
    /// </summary>
    [Fact]
    public async Task SecureHandler_RejectsLoopbackAtConnectTime()
    {
        using var client = new HttpClient(SsrfSafeConnect.CreateHandler(allowPrivate: false));

        // Port 1 is never listening; if the guard did NOT fire we'd get a
        // connection-refused. The guard must fire first with its own message.
        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetAsync("http://127.0.0.1:1/"));

        Assert.Contains("non-public", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SecureHandler_RejectsLinkLocalMetadataAddress()
    {
        using var client = new HttpClient(SsrfSafeConnect.CreateHandler(allowPrivate: false));

        // 169.254.169.254 — the cloud metadata endpoint, the classic SSRF target
        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetAsync("http://169.254.169.254/latest/meta-data/"));

        Assert.Contains("non-public", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PermissiveHandler_AllowsPrivateThroughGuard()
    {
        // allowPrivate: true (dev/LAN) must NOT reject on address grounds — it
        // reaches the actual connect, which refuses (nothing listening on :1).
        using var client = new HttpClient(SsrfSafeConnect.CreateHandler(allowPrivate: true));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetAsync("http://127.0.0.1:1/"));

        Assert.DoesNotContain("non-public", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IsPublicAddress_ClassifiesRanges()
    {
        Assert.False(OutboundUrlSafetyValidator.IsPublicAddress(IPAddress.Parse("127.0.0.1")));
        Assert.False(OutboundUrlSafetyValidator.IsPublicAddress(IPAddress.Parse("10.1.2.3")));
        Assert.False(OutboundUrlSafetyValidator.IsPublicAddress(IPAddress.Parse("192.168.0.1")));
        Assert.False(OutboundUrlSafetyValidator.IsPublicAddress(IPAddress.Parse("169.254.169.254")));
        Assert.False(OutboundUrlSafetyValidator.IsPublicAddress(IPAddress.Parse("::1")));
        Assert.True(OutboundUrlSafetyValidator.IsPublicAddress(IPAddress.Parse("1.1.1.1")));
        Assert.True(OutboundUrlSafetyValidator.IsPublicAddress(IPAddress.Parse("8.8.8.8")));
    }
}
