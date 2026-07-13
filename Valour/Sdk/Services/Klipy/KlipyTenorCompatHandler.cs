using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Valour.Sdk.Services.Klipy;

/// <summary>
/// Tenor's API is shutting down, and the server side of Valour hasn't migrated to a
/// replacement yet. Klipy publishes an official Tenor-compatible migration path: existing
/// Tenor v2 API calls (same paths, same query params, same response schema) keep working
/// as long as the host and API key are swapped to Klipy's - see
/// https://github.com/KLIPY-com/Migrate-From-Tenor-To-Klipy.
///
/// This handler rewrites outgoing Tenor v2 requests onto Klipy in flight, so the existing
/// Valour.TenorTwo.TenorClient integration (search and registershare) keeps working
/// completely unchanged. This is a stopgap for the client only - once the server has its
/// own Klipy integration, this handler and the TenorClient usage in TenorService should be
/// removed in favor of real server endpoints.
/// </summary>
public class KlipyTenorCompatHandler : DelegatingHandler
{
    private const string TenorHost = "tenor.googleapis.com";
    private const string KlipyHost = "api.klipy.com";

    private readonly string _tenorKey;
    private readonly string _klipyKey;

    public KlipyTenorCompatHandler(string tenorKey, string klipyKey, HttpMessageHandler innerHandler)
        : base(innerHandler)
    {
        _tenorKey = tenorKey;
        _klipyKey = klipyKey;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri;
        if (uri is not null && uri.Host.Equals(TenorHost, StringComparison.OrdinalIgnoreCase))
        {
            // OriginalString avoids Uri re-canonicalizing/re-escaping the string on
            // ToString() - every caller here builds the request from a plain string, so
            // there's nothing for that extra pass to normalize.
            var rewritten = uri.OriginalString
                .Replace(TenorHost, KlipyHost)
                .Replace(_tenorKey, _klipyKey);

            request.RequestUri = new Uri(rewritten);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
