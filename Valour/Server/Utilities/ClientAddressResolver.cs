using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Http;

namespace Valour.Server.Utilities;

public static class ClientAddressResolver
{
    private const string CloudflareConnectingIpHeader = "CF-Connecting-IP";
    private const string ForwardedForHeader = "X-Forwarded-For";

    /// <summary>
    /// Configuration key that makes the resolver accept CF-Connecting-IP from a
    /// local reverse proxy. Only enable it when that proxy overwrites or clears
    /// the header on every request (see Docs/Deployment/nginx.conf); otherwise
    /// anyone who reaches the proxy directly can pick their own address.
    /// </summary>
    public const string TrustCloudflareHeaderSetting = "Proxy:TrustCloudflareConnectingIp";

    /// <summary>
    /// Whether CF-Connecting-IP is trusted when the socket peer is a local proxy.
    /// Set once at startup from <see cref="TrustCloudflareHeaderSetting"/>.
    /// </summary>
    public static bool TrustCloudflareHeader { get; set; }

    /// <summary>
    /// Cloudflare's published proxy ranges (https://www.cloudflare.com/ips/).
    /// A peer or forwarded hop in these ranges is a Cloudflare edge, not a user.
    /// </summary>
    private static readonly IPNetwork[] CloudflareNetworks =
    [
        IPNetwork.Parse("173.245.48.0/20"),
        IPNetwork.Parse("103.21.244.0/22"),
        IPNetwork.Parse("103.22.200.0/22"),
        IPNetwork.Parse("103.31.4.0/22"),
        IPNetwork.Parse("141.101.64.0/18"),
        IPNetwork.Parse("108.162.192.0/18"),
        IPNetwork.Parse("190.93.240.0/20"),
        IPNetwork.Parse("188.114.96.0/20"),
        IPNetwork.Parse("197.234.240.0/22"),
        IPNetwork.Parse("198.41.128.0/17"),
        IPNetwork.Parse("162.158.0.0/15"),
        IPNetwork.Parse("104.16.0.0/13"),
        IPNetwork.Parse("104.24.0.0/14"),
        IPNetwork.Parse("172.64.0.0/13"),
        IPNetwork.Parse("131.0.72.0/22"),
        IPNetwork.Parse("2400:cb00::/32"),
        IPNetwork.Parse("2606:4700::/32"),
        IPNetwork.Parse("2803:f800::/32"),
        IPNetwork.Parse("2405:b500::/32"),
        IPNetwork.Parse("2405:8100::/32"),
        IPNetwork.Parse("2a06:98c0::/29"),
        IPNetwork.Parse("2c0f:f248::/32"),
    ];

    public static void Configure(IConfiguration configuration)
    {
        TrustCloudflareHeader = configuration.GetValue<bool>(TrustCloudflareHeaderSetting);
    }

    public static string GetClientAddress(HttpContext context) =>
        GetClientAddress(context, TrustCloudflareHeader);

    public static string GetClientAddress(HttpContext context, bool trustCloudflareHeader) =>
        ResolveClientAddress(context, trustCloudflareHeader)?.ToString() ?? "UNKNOWN";

    /// <summary>
    /// Returns the key used to group requests for rate limiting. IPv6 clients
    /// usually control a whole /64, so they are grouped by that prefix rather
    /// than by the exact address they can rotate freely.
    /// </summary>
    public static string GetRateLimitKey(HttpContext context)
    {
        var address = ResolveClientAddress(context, TrustCloudflareHeader);
        if (address is null)
            return "UNKNOWN";

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
            return address.ToString();

        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return new IPAddress(bytes) + "/64";
    }

    private static IPAddress? ResolveClientAddress(HttpContext context, bool trustCloudflareHeader)
    {
        var remoteAddress = Normalize(context.Connection.RemoteIpAddress);
        if (remoteAddress is null)
            return null;

        // Cloudflare always overwrites CF-Connecting-IP, so it can be believed
        // when the connection itself comes from a Cloudflare edge.
        if (IsCloudflare(remoteAddress))
        {
            return TryParsePublicAddress(context.Request.Headers[CloudflareConnectingIpHeader], out var edgeClient)
                ? edgeClient
                : remoteAddress;
        }

        // Proxy headers are attacker-controlled on a direct connection. Only
        // accept them when the socket peer is a local/private reverse proxy,
        // which covers the normal Docker, ingress, and same-host proxy setups.
        if (!IsPrivateOrLocal(remoteAddress))
            return remoteAddress;

        // A local proxy passes CF-Connecting-IP through untouched unless it is
        // configured to overwrite it, so it is only used when the operator says so.
        if (trustCloudflareHeader &&
            TryParsePublicAddress(context.Request.Headers[CloudflareConnectingIpHeader], out var cloudflareAddress))
            return cloudflareAddress;

        // Walk X-Forwarded-For from the right, which is the end our own proxies
        // append to. Local hops are our infrastructure. A Cloudflare hop means
        // the proxy was reached through Cloudflare, and the entry Cloudflare
        // appended just before it is the client it saw.
        var forwardedFor = context.Request.Headers[ForwardedForHeader].ToString();
        var hops = forwardedFor.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = hops.Length - 1; i >= 0; i--)
        {
            if (!TryParsePublicAddress(hops[i], out var forwardedAddress))
                continue;

            if (!IsCloudflare(forwardedAddress))
                return forwardedAddress;

            return i > 0 && TryParsePublicAddress(hops[i - 1], out var behindCloudflare)
                ? behindCloudflare
                : forwardedAddress;
        }

        return remoteAddress;
    }

    private static bool TryParsePublicAddress(string? value, out IPAddress address)
    {
        if (IPAddress.TryParse(value?.Trim().Trim('"'), out var parsed))
        {
            parsed = Normalize(parsed)!;
            if (!IsPrivateOrLocal(parsed))
            {
                address = parsed;
                return true;
            }
        }

        address = IPAddress.None;
        return false;
    }

    private static IPAddress? Normalize(IPAddress? address) =>
        address?.IsIPv4MappedToIPv6 == true ? address.MapToIPv4() : address;

    private static bool IsCloudflare(IPAddress address)
    {
        foreach (var network in CloudflareNetworks)
        {
            if (network.Contains(address))
                return true;
        }

        return false;
    }

    private static bool IsPrivateOrLocal(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) ||
            address.Equals(IPAddress.Any) ||
            address.Equals(IPAddress.None) ||
            address.Equals(IPAddress.IPv6Any) ||
            address.Equals(IPAddress.IPv6None) ||
            address.IsIPv6LinkLocal ||
            address.IsIPv6Multicast ||
            address.IsIPv6SiteLocal)
        {
            return true;
        }

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
            return (bytes[0] & 0xfe) == 0xfc; // fc00::/7 unique-local addresses

        return bytes[0] == 0 ||
               bytes[0] == 10 ||
               bytes[0] == 127 ||
               (bytes[0] == 100 && bytes[1] is >= 64 and <= 127) ||
               (bytes[0] == 169 && bytes[1] == 254) ||
               (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
               (bytes[0] == 192 && bytes[1] == 168) ||
               (bytes[0] == 198 && bytes[1] is 18 or 19) ||
               bytes[0] >= 224;
    }
}
