using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace Valour.Server.Cdn;

/// <summary>
/// Validates untrusted outbound URLs before server-side fetch/proxy operations.
/// Blocks localhost/private/link-local/reserved destinations to reduce SSRF risk.
/// </summary>
public static class OutboundUrlSafetyValidator
{
    public static async Task<bool> IsSafeAsync(string rawUrl, ILogger? logger = null)
    {
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
            return false;

        return await IsSafeAsync(uri, logger);
    }

    public static async Task<bool> IsSafeAsync(Uri uri, ILogger? logger = null)
    {
        if (uri is null || !uri.IsAbsoluteUri)
            return false;

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return false;

        if (!string.IsNullOrWhiteSpace(uri.UserInfo))
            return false;

        var host = uri.DnsSafeHost;
        if (string.IsNullOrWhiteSpace(host) || IsLocalHostName(host))
            return false;

        if (IPAddress.TryParse(host, out var parsed))
            return !IsPrivateOrReservedAddress(parsed);

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host);
            if (addresses.Length == 0)
                return false;

            // Require all resolved addresses to be public to avoid private-IP fallback/rebinding.
            return addresses.All(address => !IsPrivateOrReservedAddress(address));
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to resolve outbound host {Host}", host);
            return false;
        }
    }

    private static bool IsLocalHostName(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            return true;

        if (host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
            return true;

        if (host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static bool IsPrivateOrReservedAddress(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();

        if (IPAddress.IsLoopback(ip) ||
            ip.Equals(IPAddress.Any) ||
            ip.Equals(IPAddress.None) ||
            ip.Equals(IPAddress.IPv6Any) ||
            ip.Equals(IPAddress.IPv6None))
        {
            return true;
        }

        return ip.AddressFamily switch
        {
            AddressFamily.InterNetwork => IsPrivateOrReservedIpv4(ip.GetAddressBytes()),
            AddressFamily.InterNetworkV6 => IsPrivateOrReservedIpv6(ip),
            _ => true
        };
    }

    private static bool IsPrivateOrReservedIpv4(byte[] bytes)
    {
        if (bytes.Length != 4)
            return true;

        var b0 = bytes[0];
        var b1 = bytes[1];
        var b2 = bytes[2];

        if (b0 == 10) return true; // 10.0.0.0/8
        if (b0 == 127) return true; // 127.0.0.0/8
        if (b0 == 0) return true; // 0.0.0.0/8
        if (b0 == 169 && b1 == 254) return true; // 169.254.0.0/16 (link-local)
        if (b0 == 172 && b1 is >= 16 and <= 31) return true; // 172.16.0.0/12
        if (b0 == 192 && b1 == 168) return true; // 192.168.0.0/16
        if (b0 == 100 && b1 is >= 64 and <= 127) return true; // 100.64.0.0/10
        if (b0 == 192 && b1 == 0 && b2 == 0) return true; // 192.0.0.0/24
        if (b0 == 192 && b1 == 0 && b2 == 2) return true; // 192.0.2.0/24 (TEST-NET-1)
        if (b0 == 198 && b1 is 18 or 19) return true; // 198.18.0.0/15
        if (b0 == 198 && b1 == 51 && b2 == 100) return true; // 198.51.100.0/24 (TEST-NET-2)
        if (b0 == 203 && b1 == 0 && b2 == 113) return true; // 203.0.113.0/24 (TEST-NET-3)
        if (b0 >= 224) return true; // multicast and reserved blocks

        return false;
    }

    private static bool IsPrivateOrReservedIpv6(IPAddress ip)
    {
        if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast)
            return true;

        var bytes = ip.GetAddressBytes();
        if (bytes.Length != 16)
            return true;

        // fc00::/7 (Unique local)
        if ((bytes[0] & 0xFE) == 0xFC)
            return true;

        // fe80::/10 (link local)
        if (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80)
            return true;

        // 2001:db8::/32 (documentation)
        if (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0D && bytes[3] == 0xB8)
            return true;

        return false;
    }
}
