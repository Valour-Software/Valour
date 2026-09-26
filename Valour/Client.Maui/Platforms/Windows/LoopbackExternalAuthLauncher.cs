using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Valour.Client.Device;
using Valour.Shared.Models;

namespace Valour.Client.Maui;

/// <summary>
/// Signs in with a provider in the default browser and receives the result on
/// a temporary loopback port. The unpackaged Windows app can't register a URL
/// scheme the way the Android app does.
/// </summary>
public class LoopbackExternalAuthLauncher : IExternalAuthLauncher
{
    private static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(10);

    public ExternalAuthClient Client => ExternalAuthClient.Desktop;

    public ExternalAuthLaunch Prepare()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return new Launch(listener);
    }

    private sealed class Launch(TcpListener listener) : ExternalAuthLaunch
    {
        public override int? LoopbackPort => ((IPEndPoint)listener.LocalEndpoint).Port;

        public override async Task<ExternalAuthResultResponse> WaitAsync(
            ExternalAuthBeginResponse begin, string verifier, CancellationToken cancellationToken)
        {
            await Launcher.Default.OpenAsync(new Uri(begin.AuthorizationUrl));

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(MaxWait);

            try
            {
                while (true)
                {
                    using var client = await listener.AcceptTcpClientAsync(timeout.Token);
                    await using var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);

                    // Only the request line matters: "GET /callback?result=... HTTP/1.1"
                    var requestLine = await reader.ReadLineAsync(timeout.Token) ?? string.Empty;
                    var parts = requestLine.Split(' ');
                    var target = parts.Length >= 2 ? parts[1] : string.Empty;

                    if (!target.StartsWith("/callback?", StringComparison.Ordinal))
                    {
                        await RespondAsync(stream, "404 Not Found", string.Empty);
                        continue;
                    }

                    var query = QueryHelpers.ParseQuery(target["/callback".Length..]);
                    await RespondAsync(stream, "200 OK",
                        "<!doctype html><html><head><meta charset=\"utf-8\"><title>Valour</title></head>" +
                        "<body style=\"background:#040d14;color:#fff;font-family:sans-serif\">" +
                        "<p>Done! You can close this tab and return to Valour.</p></body></html>");

                    return new ExternalAuthResultResponse
                    {
                        Result = query.TryGetValue("result", out var result) ? result.ToString() : null,
                        Ticket = query.TryGetValue("ticket", out var ticket) ? ticket.ToString() : null,
                        Message = query.TryGetValue("message", out var message) ? message.ToString() : null,
                    };
                }
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        private static async Task RespondAsync(Stream stream, string status, string html)
        {
            var body = Encoding.UTF8.GetBytes(html);
            var header = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {status}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\n" +
                "Cache-Control: no-store\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(header);
            await stream.WriteAsync(body);
        }

        public override ValueTask DisposeAsync()
        {
            listener.Stop();
            return ValueTask.CompletedTask;
        }
    }
}
