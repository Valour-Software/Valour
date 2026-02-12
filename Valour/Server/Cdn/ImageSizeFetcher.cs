using System.Buffers;
using SixLabors.ImageSharp;
using Microsoft.Extensions.Logging;

namespace Valour.Server.Cdn;

public class ImageSizeFetcher
{
    /// <summary>
    /// Reads the size of an image file at a url without fetching more bytes than necessary.
    /// </summary>
    public static async Task<(int width, int height, string format)?> GetImageDimensionsAsync(
        string url,
        HttpClient? client = null,
        ILogger? logger = null,
        int maxBytes = 32768)
    {
        if (!await OutboundUrlSafetyValidator.IsSafeAsync(url, logger))
            return null;

        var ownsClient = client is null;
        client ??= new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false
        });

        var buffer = ArrayPool<byte>.Shared.Rent(maxBytes);
        int bytesFetched = 0;

        // Define the initial chunk sizes
        int[] initialChunks = { 128, 1024, 4096 };
        int chunkIndex = 0;
        int currentChunkSize = initialChunks[0];

        try
        {
            while (bytesFetched < maxBytes)
            {
                // Determine chunk size for this iteration
                if (chunkIndex < initialChunks.Length)
                {
                    currentChunkSize = initialChunks[chunkIndex];
                }
                else
                {
                    currentChunkSize *= 2;
                    // Don't exceed maxBytes
                    if (bytesFetched + currentChunkSize > maxBytes)
                        currentChunkSize = maxBytes - bytesFetched;
                }

                int rangeStart = bytesFetched;
                int rangeEnd = Math.Min(bytesFetched + currentChunkSize - 1, maxBytes - 1);

                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(rangeStart, rangeEnd);

                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                if (!response.IsSuccessStatusCode)
                    return null;

                var bytes = await response.Content.ReadAsByteArrayAsync();
                if (bytes.Length == 0)
                    break;

                bytes.AsSpan().CopyTo(buffer.AsSpan(bytesFetched));
                bytesFetched += bytes.Length;

                using var ms = new MemoryStream(buffer, 0, bytesFetched, writable: false, publiclyVisible: true);
                try
                {
                    var info = await Image.IdentifyAsync(ms);
                    if (info is not null)
                        return (info.Width, info.Height, info.Metadata?.DecodedImageFormat?.Name);
                }
                catch
                {
                    // Ignore and try with more bytes
                }

                if (bytes.Length < currentChunkSize)
                    break;

                chunkIndex++;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            if (ownsClient)
                client.Dispose();
        }

        // Gave up after maxBytes
        return null;
    }
}
