using System.Net;
using Microsoft.AspNetCore.Http;

namespace Valour.Server.Utilities;

public static class ClientAddressResolver
{
    private const string CloudflareConnectingIpHeader = "CF-Connecting-IP";
    private const string ForwardedForHeader = "X-Forwarded-For";

    public static string GetClientAddress(HttpContext context)
    {
        var remoteAddress = Normalize(context.Connection.RemoteIpAddress);
        if (remoteAddress is null)
            return "UNKNOWN";

        // Proxy headers are attacker-controlled on a direct connection. Only
        // accept them when the socket peer is a local/private reverse proxy,
        // which covers the normal Docker, ingress, and same-host proxy setups.
        if (!IsPrivateOrLocal(remoteAddress))
            return remoteAddress.ToString();

        if (TryParsePublicAddress(context.Request.Headers[CloudflareConnectingIpHeader], out var cloudflareAddress))
            return cloudflareAddress.ToString();

        var forwardedFor = context.Request.Headers[ForwardedForHeader].ToString();
        foreach (var value in forwardedFor.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Reverse())
        {
            if (TryParsePublicAddress(value, out var forwardedAddress))
                return forwardedAddress.ToString();
        }

        return remoteAddress.ToString();
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
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
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
