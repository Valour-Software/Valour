using System.Text.Json;
using Valour.BuildTools.VillageAtlasPacker;

namespace Valour.Tests.Client;

public class VillageAtlasProtectionTests
{
    [Fact]
    public void Encoder_MatchesTheBrowserDecoderVectorAndIsDeterministic()
    {
        using var stream = typeof(VillageAtlasProtectionTests).Assembly.GetManifestResourceStream("Valour.Tests.village-atlas-vector.json")!;
        using var vector = JsonDocument.Parse(stream);
        var png = Convert.FromBase64String(vector.RootElement.GetProperty("png").GetString()!);
        var expected = Convert.FromBase64String(vector.RootElement.GetProperty("protected").GetString()!);
        Assert.Equal(expected, AtlasEnvelope.Encode(png));
        Assert.Equal(expected, AtlasEnvelope.Encode(png));
        Assert.Equal(png.Length + 20, expected.Length);
        Assert.True(expected.AsSpan().IndexOf(png.AsSpan(0, 8)) < 0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(23)]
    [InlineData(30)]
    [InlineData(16 * 1024 * 1024 + 1)]
    public void Encoder_RejectsMissingMalformedAndOversizedImages(int length)
    {
        var input = new byte[length];
        if (length > AtlasEnvelope.MaximumImageBytes)
            new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(input, 0);
        Assert.Throws<InvalidDataException>(() => AtlasEnvelope.Encode(input));
    }
}
