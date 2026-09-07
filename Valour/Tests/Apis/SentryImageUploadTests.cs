using System.Net;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Valour.Tests.Apis;

[Collection("ApiCollection")]
public class SentryImageUploadTests(LoginTestFixture fixture)
{
    [Fact]
    public async Task CorruptImageWithValidHeader_ReturnsBadRequest()
    {
        using var image = new Image<Rgba32>(32, 32);
        await using var encoded = new MemoryStream();
        await image.SaveAsPngAsync(encoded);
        var png = encoded.ToArray();
        // Keep the PNG chunks and metadata valid, but invalidate the IDAT zlib header.
        var offset = 8;
        while (System.Text.Encoding.ASCII.GetString(png, offset + 4, 4) != "IDAT")
            offset += 12 + System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset, 4));
        png[offset + 8] = 0;
        var length = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset, 4));
        uint crc = uint.MaxValue;
        foreach (var value in png.AsSpan(offset + 4, length + 4))
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320u);
        }
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(offset + 8 + length, 4), ~crc);
        Assert.NotNull(Image.Identify(png));
        Assert.ThrowsAny<ImageFormatException>(() => Image.Load(png));

        using var form = new MultipartFormDataContent();
        using var file = new ByteArrayContent(png);
        file.Headers.ContentType = new("image/png");
        form.Add(file, "avatar", "corrupt.png");
        using var response = await fixture.Client.Http.PostAsync("upload/profile", form);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("damaged", await response.Content.ReadAsStringAsync());
    }
}
