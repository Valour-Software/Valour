using Valour.Client.Utility;

namespace Valour.Tests.Utilities;

public class JpegMetadataStripperTests
{
    private static byte[] Segment(byte marker, byte[] payload)
    {
        var length = payload.Length + 2;
        var result = new byte[4 + payload.Length];
        result[0] = 0xFF;
        result[1] = marker;
        result[2] = (byte)(length >> 8);
        result[3] = (byte)(length & 0xFF);
        payload.CopyTo(result, 4);
        return result;
    }

    private static byte[] BuildJpeg(params byte[][] parts)
    {
        var sos = new byte[] { 0xFF, 0xDA, 0x00, 0x04, 0x01, 0x02, 0xAB, 0xCD, 0xFF, 0xD9 };
        var all = new List<byte> { 0xFF, 0xD8 };
        foreach (var part in parts)
            all.AddRange(part);
        all.AddRange(sos);
        return all.ToArray();
    }

    [Fact]
    public void Strip_RemovesExifApp1_KeepsJfifApp0AndImageData()
    {
        var app0 = Segment(0xE0, "JFIF\0"u8.ToArray());
        var exif = Segment(0xE1, "Exif\0\0SECRET-GPS-DATA"u8.ToArray());
        var quant = Segment(0xDB, new byte[] { 0x01, 0x02 });

        var jpeg = BuildJpeg(app0, exif, quant);
        var stripped = JpegMetadataStripper.Strip(jpeg);

        Assert.NotEqual(jpeg, stripped);
        // EXIF payload gone
        Assert.False(ContainsSequence(stripped, "SECRET-GPS-DATA"u8.ToArray()));
        // JFIF and structural segments remain
        Assert.True(ContainsSequence(stripped, "JFIF"u8.ToArray()));
        // Entropy-coded data intact (0xAB 0xCD before EOI)
        Assert.Equal(0xD9, stripped[^1]);
        Assert.Equal(0xFF, stripped[^2]);
    }

    [Fact]
    public void Strip_RemovesXmpAndIccSegments()
    {
        var xmp = Segment(0xE1, "http://ns.adobe.com/xap/1.0/\0<xmp/>"u8.ToArray());
        var icc = Segment(0xE2, "ICC_PROFILE\0"u8.ToArray());
        var jpeg = BuildJpeg(xmp, icc);

        var stripped = JpegMetadataStripper.Strip(jpeg);

        Assert.False(ContainsSequence(stripped, "adobe"u8.ToArray()));
        Assert.False(ContainsSequence(stripped, "ICC_PROFILE"u8.ToArray()));
    }

    [Fact]
    public void Strip_NonJpeg_ReturnsInputUnchanged()
    {
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02 };
        Assert.Same(png, JpegMetadataStripper.Strip(png));
    }

    [Fact]
    public void Strip_MalformedJpeg_ReturnsInputUnchanged()
    {
        // SOI then garbage that is not a valid marker
        var malformed = new byte[] { 0xFF, 0xD8, 0x00, 0x01, 0x02, 0x03, 0x04, 0x05 };
        Assert.Same(malformed, JpegMetadataStripper.Strip(malformed));
    }

    [Fact]
    public void Strip_TruncatedSegmentLength_ReturnsInputUnchanged()
    {
        // APP1 claiming a length longer than the buffer
        var truncated = new byte[] { 0xFF, 0xD8, 0xFF, 0xE1, 0xFF, 0xFF, 0x01 };
        Assert.Same(truncated, JpegMetadataStripper.Strip(truncated));
    }

    private static bool ContainsSequence(byte[] haystack, byte[] needle)
    {
        return haystack.AsSpan().IndexOf(needle) >= 0;
    }
}
