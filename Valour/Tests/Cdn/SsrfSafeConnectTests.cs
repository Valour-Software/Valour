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

    [Theory]
    [InlineData("64:ff9b::7f00:1")] // NAT64 of 127.0.0.1
    [InlineData("64:ff9b::a9fe:a9fe")] // NAT64 of 169.254.169.254
    [InlineData("64:ff9b:1::808:808")] // local-use NAT64
    [InlineData("2002:a00:1::")] // 6to4 of 10.0.0.1
    [InlineData("2002:7f00:1::1")] // 6to4 of 127.0.0.1
    [InlineData("2001:0:4136:e378:8000:63bf:3fff:fdd2")] // Teredo
    [InlineData("::7f00:1")] // IPv4-compatible 127.0.0.1
    [InlineData("::808:808")] // IPv4-compatible (deprecated form)
    [InlineData("::ffff:10.0.0.1")] // IPv4-mapped
    public void IsPublicAddress_BlocksEmbeddedPrivateIpv4(string address)
    {
        Assert.False(OutboundUrlSafetyValidator.IsPublicAddress(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("64:ff9b::808:808")] // NAT64 of 8.8.8.8
    [InlineData("2002:808:808::1")] // 6to4 of 8.8.8.8
    [InlineData("2606:4700:4700::1111")]
    public void IsPublicAddress_AllowsEmbeddedPublicIpv4(string address)
    {
        Assert.True(OutboundUrlSafetyValidator.IsPublicAddress(IPAddress.Parse(address)));
    }

    [Fact]
    public void Handler_IgnoresAmbientProxy()
    {
        using var handler = SsrfSafeConnect.CreateHandler(allowPrivate: false);

        Assert.False(handler.UseProxy);
        Assert.False(handler.AllowAutoRedirect);
    }
}
