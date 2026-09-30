using Valour.Client.Utility;

namespace Valour.Tests.Client;

public class NebulaPaletteTests
{
    // The same values are asserted in Tests/Js/nebula.test.mjs, so the C# and
    // JavaScript palette choices cannot drift apart.
    [Theory]
    [InlineData("12215159187308544", 1317615479u, "tarantula")]
    [InlineData("47213464583929856", 3563017055u, "tarantula")]
    [InlineData("planet-7", 3238120247u, "veil")]
    [InlineData("12215159187308544~1", 1301529070u, "eagle")]
    [InlineData("", 2166136261u, "orion")]
    public void MatchesTheJavaScriptPalettes(string seed, uint hash, string palette)
    {
        Assert.Equal(hash, NebulaPalettes.HashSeed(seed));
        Assert.Equal(palette, NebulaPalettes.PaletteFor(seed));
    }

    [Fact]
    public void PlanetIdsUseTheirDecimalForm()
    {
        Assert.Equal(NebulaPalettes.PaletteFor("12215159187308544"), NebulaPalettes.PaletteFor(12215159187308544L, 0));
    }

    [Fact]
    public void TheOriginalWorldIsSeededByTheIdAlone()
    {
        Assert.Equal("12215159187308544", NebulaPalettes.WorldSeed(12215159187308544L, 0));
        Assert.Equal("12215159187308544~1", NebulaPalettes.WorldSeed(12215159187308544L, 1));
        Assert.Equal("12215159187308544~255", NebulaPalettes.WorldSeed(12215159187308544L, 255));
        Assert.Equal(NebulaPalettes.PlanetLight("12215159187308544~1"), NebulaPalettes.PlanetLight(12215159187308544L, 1));
    }

    [Fact]
    public void RegeneratingCyclesWithoutReturningToTheOriginal()
    {
        Assert.Equal(1, NebulaPalettes.NextWorldVariant(0));
        Assert.Equal(2, NebulaPalettes.NextWorldVariant(1));
        Assert.Equal(1, NebulaPalettes.NextWorldVariant(255));
    }

    [Fact]
    public void BrandPaletteIsNeverAssigned()
    {
        Assert.DoesNotContain("valour", NebulaPalettes.PlanetPalettes);
    }

    [Fact]
    public void HuesMatchTheJavaScriptPalettes()
    {
        // The same table is asserted in Tests/Js/nebula.test.mjs.
        Assert.Equal([205, 280, 350], NebulaPalettes.Hues["carina"]);
        Assert.Equal([30, 60, 210], NebulaPalettes.Hues["crab"]);
        Assert.Equal([180, 290, 45], NebulaPalettes.Hues["tarantula"]);
        Assert.Equal([300, 265, 205], NebulaPalettes.Hues["valour"]);
        Assert.All(NebulaPalettes.PlanetPalettes, name => Assert.True(NebulaPalettes.Hues.ContainsKey(name)));
    }

    [Fact]
    public void PlanetLightUsesTheCoreHueOfThePlanetsPalette()
    {
        Assert.Equal("oklch(0.82 0.1 45)", NebulaPalettes.PlanetLight("12215159187308544"));
        Assert.Equal(NebulaPalettes.PlanetLight("planet-7"), NebulaPalettes.PlanetLight("planet-7"));
    }

    [Theory]
    // Reference values from CSS Color 4 conversions.
    [InlineData(1.0, 0.0, 0.0, "#ffffff")]
    [InlineData(0.0, 0.0, 0.0, "#000000")]
    [InlineData(0.627955, 0.257683, 29.2339, "#ff0000")]
    [InlineData(0.519752, 0.176858, 142.495, "#008000")]
    public void OklchToHex_MatchesCssConversions(double lightness, double chroma, double hue, string expected)
    {
        Assert.Equal(expected, NebulaPalettes.OklchToHex(lightness, chroma, hue));
    }
}
