using System.Net;
using Microsoft.AspNetCore.Http;
using Valour.Server.Utilities;

namespace Valour.Tests.Server;

public class ClientAddressResolverTests
{
    [Fact]
    public void GetClientAddress_PrefersCloudflareHeaderBehindPrivateProxyWhenTrusted()
    {
        var context = CreateContext("172.18.0.2");
        context.Request.Headers["CF-Connecting-IP"] = "8.8.8.8";
        context.Request.Headers["X-Forwarded-For"] = "1.1.1.1, 9.9.9.9";

        Assert.Equal("8.8.8.8", ClientAddressResolver.GetClientAddress(context, trustCloudflareHeader: true));
    }

    [Fact]
    public void GetClientAddress_IgnoresCloudflareHeaderBehindPrivateProxyByDefault()
    {
        var context = CreateContext("172.18.0.2");
        context.Request.Headers["CF-Connecting-IP"] = "8.8.8.8";
        context.Request.Headers["X-Forwarded-For"] = "9.9.9.9";

        Assert.Equal("9.9.9.9", ClientAddressResolver.GetClientAddress(context, trustCloudflareHeader: false));
    }

    [Fact]
    public void GetClientAddress_TrustsCloudflareHeaderFromCloudflarePeer()
    {
        var context = CreateContext("162.158.10.20");
        context.Request.Headers["CF-Connecting-IP"] = "8.8.8.8";

        Assert.Equal("8.8.8.8", ClientAddressResolver.GetClientAddress(context));
    }

    [Fact]
    public void GetClientAddress_UsesAddressBeforeCloudflareHopInForwardedFor()
    {
        // Cloudflare appends the client it saw; the local proxy then appends
        // the Cloudflare edge. Anything further left is client-supplied.
        var context = CreateContext("10.0.0.5");
        context.Request.Headers["X-Forwarded-For"] = "5.5.5.5, 8.8.8.8, 162.158.10.20";

        Assert.Equal("8.8.8.8", ClientAddressResolver.GetClientAddress(context));
    }

    [Fact]
    public void GetRateLimitKey_GroupsIpv6AddressesBySlash64()
    {
        var first = CreateContext("2001:db8:1:2:aaaa:bbbb:cccc:dddd");
        var second = CreateContext("2001:db8:1:2::1");
        var other = CreateContext("2001:db8:1:3::1");

        Assert.Equal("2001:db8:1:2::/64", ClientAddressResolver.GetRateLimitKey(first));
        Assert.Equal(ClientAddressResolver.GetRateLimitKey(first), ClientAddressResolver.GetRateLimitKey(second));
        Assert.NotEqual(ClientAddressResolver.GetRateLimitKey(first), ClientAddressResolver.GetRateLimitKey(other));
    }

    [Fact]
    public void GetRateLimitKey_UsesFullIpv4Address()
    {
        Assert.Equal("8.8.8.8", ClientAddressResolver.GetRateLimitKey(CreateContext("8.8.8.8")));
    }

    [Fact]
    public void GetClientAddress_UsesRightmostPublicForwardedAddressBehindPrivateProxy()
    {
        var context = CreateContext("10.0.0.5");
        context.Request.Headers["X-Forwarded-For"] = "1.1.1.1, 192.168.1.20, 9.9.9.9";

        Assert.Equal("9.9.9.9", ClientAddressResolver.GetClientAddress(context));
    }

    [Fact]
    public void GetClientAddress_IgnoresSpoofedHeadersOnDirectPublicConnection()
    {
        var context = CreateContext("8.8.4.4");
        context.Request.Headers["CF-Connecting-IP"] = "1.1.1.1";
        context.Request.Headers["X-Forwarded-For"] = "9.9.9.9";

        Assert.Equal("8.8.4.4", ClientAddressResolver.GetClientAddress(context));
    }

    [Fact]
    public void GetClientAddress_RejectsPrivateForwardedAddresses()
    {
        var context = CreateContext("172.18.0.2");
        context.Request.Headers["CF-Connecting-IP"] = "192.168.1.20";
        context.Request.Headers["X-Forwarded-For"] = "10.0.0.8";

        Assert.Equal("172.18.0.2", ClientAddressResolver.GetClientAddress(context));
    }

    [Fact]
    public void GetClientAddress_NormalizesIpv4MappedAddress()
    {
        var context = CreateContext("::ffff:8.8.8.8");

        Assert.Equal("8.8.8.8", ClientAddressResolver.GetClientAddress(context));
    }

    [Fact]
    public void GetClientAddress_ReturnsUnknownWithoutSocketAddress()
    {
        Assert.Equal("UNKNOWN", ClientAddressResolver.GetClientAddress(new DefaultHttpContext()));
    }

    private static DefaultHttpContext CreateContext(string remoteAddress)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(remoteAddress);
        return context;
    }
}
