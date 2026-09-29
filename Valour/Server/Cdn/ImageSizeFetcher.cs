using System.Buffers;
using System.Net;
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
        int maxBytes = 32768,
        CancellationToken cancellationToken = default)
    {
        if (!await OutboundUrlSafetyValidator.IsSafeAsync(url, logger))
            return null;

        var ownsClient = client is null;
        // IsSafeAsync resolves the host to validate it, but a plain handler
        // resolves again at connect - so the fallback client must pin to the
        // validated address or the check can be defeated by DNS rebinding.
        client ??= new HttpClient(SsrfSafeConnect.CreateHandler(allowPrivate: false))
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

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

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(rangeStart, rangeEnd);

                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (!response.IsSuccessStatusCode)
                    return null;

                // An origin that ignores Range answers 200 with the whole file
                // from byte zero. Read only the leading bytes that fit in the
                // buffer, identify once, and stop instead of requesting more.
                var ignoredRange = response.StatusCode != HttpStatusCode.PartialContent;
                var writeOffset = ignoredRange ? 0 : bytesFetched;
                var readLimit = ignoredRange ? maxBytes : rangeEnd - rangeStart + 1;

                await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
                var read = await ReadAtMostAsync(body, buffer.AsMemory(writeOffset, readLimit), cancellationToken);
                if (read == 0)
                    break;

                bytesFetched = writeOffset + read;

                if (TryReadPngSize(buffer.AsSpan(0, bytesFetched), out var pngWidth, out var pngHeight))
                    return (pngWidth, pngHeight, "PNG");

                using var ms = new MemoryStream(buffer, 0, bytesFetched, writable: false, publiclyVisible: true);
                try
                {
                    var info = await Image.IdentifyAsync(ms, cancellationToken);
                    if (info is not null)
                        return (info.Width, info.Height, info.Metadata?.DecodedImageFormat?.Name);
                }
                catch
                {
                    // Ignore and try with more bytes
                }

                if (ignoredRange || read < currentChunkSize)
                    break;

                chunkIndex++;
            }
        }
        catch (Exception ex)
        {
            // A broken or hostile origin only means the size is unknown; it
            // must not fail the message that linked to it.
            logger?.LogDebug(ex, "Failed to read image dimensions from {Url}", url);
            return null;
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

    /// <summary>
    /// Reads a PNG's size from its header, which is the first chunk and has
    /// a fixed layout. ImageSharp checks the image data's checksum while
    /// identifying a PNG, so it fails on the partial file read here whenever
    /// the image data starts right after the header.
    /// </summary>
    public static bool TryReadPngSize(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = height = 0;
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        ReadOnlySpan<byte> header = [0x49, 0x48, 0x44, 0x52];
        if (data.Length < 24 || !data.StartsWith(signature) || !data.Slice(12, 4).SequenceEqual(header))
            return false;

        var w = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(data.Slice(16, 4));
        var h = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(data.Slice(20, 4));
        if (w is 0 or > int.MaxValue || h is 0 or > int.MaxValue)
            return false;

        width = (int)w;
        height = (int)h;
        return true;
    }

    private static async Task<int> ReadAtMostAsync(Stream stream, Memory<byte> destination, CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < destination.Length)
        {
            var read = await stream.ReadAsync(destination[total..], cancellationToken);
            if (read == 0)
                break;

            total += read;
        }

        return total;
    }
}
