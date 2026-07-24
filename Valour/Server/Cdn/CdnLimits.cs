using System.Text;

namespace Valour.Server.Cdn;

/// <summary>
/// Caps on data pulled from third-party origins. The proxy and unfurl paths
/// fetch attacker-chosen URLs, so a hostile or huge origin must not be able to
/// buffer unbounded memory into the server.
/// </summary>
public static class CdnLimits
{
    /// <summary>
    /// Largest proxied media response we will buffer (16 MB).
    /// </summary>
    public const int MaxProxyResponseBytes = 16 * 1024 * 1024;

    /// <summary>
    /// Largest HTML document read when scraping OpenGraph tags (1 MB). Meta
    /// tags live in the head, so this is far more than needed.
    /// </summary>
    public const int MaxHtmlScrapeBytes = 1024 * 1024;

    /// <summary>
    /// Reads at most <paramref name="maxBytes"/> from the response, returning
    /// null if the origin exceeds it. Content-Length is checked first so an
    /// oversized body is rejected before any of it is read.
    /// </summary>
    public static async Task<byte[]?> ReadBoundedAsync(
        HttpContent content,
        int maxBytes,
        CancellationToken cancellationToken = default)
    {
        if (content.Headers.ContentLength > maxBytes)
            return null;

        await using var stream = await content.ReadAsStreamAsync(cancellationToken);

        // One byte over the cap is enough to know the origin exceeded it.
        var buffer = new byte[maxBytes + 1];
        var total = 0;

        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken);
            if (read == 0)
                break;

            total += read;
        }

        if (total > maxBytes)
            return null;

        return buffer[..total];
    }

    /// <summary>
    /// Bounded text read, for HTML scraping.
    /// </summary>
    public static async Task<string?> ReadBoundedStringAsync(
        HttpContent content,
        int maxBytes,
        CancellationToken cancellationToken = default)
    {
        var bytes = await ReadBoundedAsync(content, maxBytes, cancellationToken);
        return bytes is null ? null : Encoding.UTF8.GetString(bytes);
    }
}
