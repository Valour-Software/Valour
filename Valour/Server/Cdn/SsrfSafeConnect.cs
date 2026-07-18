using System.Net;
using System.Net.Sockets;

namespace Valour.Server.Cdn;

/// <summary>
/// Builds a SocketsHttpHandler ConnectCallback that resolves the target host
/// and connects to a validated IP directly — closing the DNS-rebinding window
/// that check-then-fetch leaves open (the address that is validated is the
/// exact address that is dialed, with no second resolution in between).
/// </summary>
public static class SsrfSafeConnect
{
    public static SocketsHttpHandler CreateHandler(bool allowPrivate, bool acceptAnyCertificate = false)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectCallback = async (context, cancellationToken) =>
            {
                var host = context.DnsEndPoint.Host;
                var port = context.DnsEndPoint.Port;

                IPAddress[] addresses;
                if (IPAddress.TryParse(host, out var literal))
                    addresses = new[] { literal };
                else
                    addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);

                if (addresses.Length == 0)
                    throw new HttpRequestException($"Could not resolve host '{host}'.");

                // Require every resolved address to be public (unless private is
                // explicitly allowed for dev/LAN) — an all-or-nothing check on
                // the exact resolution we then connect through (no rebinding).
                if (!allowPrivate && addresses.Any(a => !OutboundUrlSafetyValidator.IsPublicAddress(a)))
                    throw new HttpRequestException($"Host '{host}' resolves to a non-public address.");

                // Try each validated address in turn (a dual-stack host may
                // list an unreachable family first, e.g. ::1 before 127.0.0.1).
                Exception lastError = null;
                foreach (var target in addresses)
                {
                    var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                    try
                    {
                        await socket.ConnectAsync(new IPEndPoint(target, port), cancellationToken);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
                        socket.Dispose();
                    }
                }

                throw lastError ?? new HttpRequestException($"Could not connect to host '{host}'.");
            }
        };

        if (acceptAnyCertificate)
        {
            handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        }

        return handler;
    }
}
