using System.Net;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Valour.Server.Cdn;

namespace Valour.Tests.Cdn;

public class ImageSizeFetcherTests
{
    // A public address literal, so the URL check needs no DNS lookup.
    private const string Url = "https://93.184.216.34/image.png";

    [Fact]
    public async Task OriginIgnoringRange_ReadsOnlyTheBufferAndStillIdentifies()
    {
        using var image = new Image<Rgba32>(37, 21);
        using var png = new MemoryStream();
        await image.SaveAsPngAsync(png);
        var body = png.ToArray().Concat(new byte[1024 * 1024]).ToArray();

        using var client = new HttpClient(new StaticHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(body)
        }));

        var result = await ImageSizeFetcher.GetImageDimensionsAsync(Url, client);

        Assert.NotNull(result);
        Assert.Equal(37, result.Value.width);
        Assert.Equal(21, result.Value.height);
    }

    [Fact]
    public async Task OversizedNonImageBody_ReturnsUnknownInsteadOfThrowing()
    {
        using var client = new HttpClient(new StaticHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[4 * 1024 * 1024])
        }));

        Assert.Null(await ImageSizeFetcher.GetImageDimensionsAsync(Url, client));
    }

    [Fact]
    public async Task TransportFailure_ReturnsUnknownInsteadOfThrowing()
    {
        using var client = new HttpClient(new StaticHandler(() => throw new HttpRequestException("connection reset")));

        Assert.Null(await ImageSizeFetcher.GetImageDimensionsAsync(Url, client));
    }

    private sealed class StaticHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond());
    }

    [Fact]
    public void TryReadPngSize_ReadsTheHeaderOfAPartialFile()
    {
        // Signature, IHDR length and type, then 1200 x 600, cut off before the image data.
        byte[] data = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52,
            0, 0, 0x04, 0xB0, 0, 0, 0x02, 0x58, 8, 2];

        Assert.True(ImageSizeFetcher.TryReadPngSize(data, out var width, out var height));
        Assert.Equal(1200, width);
        Assert.Equal(600, height);
    }

    [Fact]
    public void TryReadPngSize_RejectsOtherData()
    {
        Assert.False(ImageSizeFetcher.TryReadPngSize("GIF89a not a png at all!"u8, out _, out _));
    }
}
