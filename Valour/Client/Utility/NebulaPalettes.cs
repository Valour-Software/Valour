using Valour.Shared.Models;

namespace Valour.Client.Utility;

/// <summary>
/// The C# side of the palette choice in wwwroot/js/nebula.js, used where markup
/// needs to name a planet's sky. Both sides must pick the same palette for the
/// same seed; NebulaPaletteTests pins them to shared values.
/// </summary>
public static class NebulaPalettes
{
    /// <summary>
    /// Palettes a planet can be given, in the order nebula.js lists them. The
    /// brand palette is not included.
    /// </summary>
    public static readonly string[] PlanetPalettes =
        ["carina", "eagle", "rosette", "crab", "helix", "veil", "lagoon", "orion", "tarantula"];

    /// <summary>
    /// The seed a planet's sky, globe, and planet light are generated from. The
    /// original world (variant 0) is seeded by the id alone, so planets that were
    /// never regenerated keep the world they always had.
    /// </summary>
    public static string WorldSeed(long planetId, byte worldVariant) =>
        worldVariant == 0 ? planetId.ToString() : $"{planetId}~{worldVariant}";

    public static string WorldSeed(ISharedPlanet planet) => WorldSeed(planet.Id, planet.WorldVariant);

    public static string WorldSeed(ISharedPlanetListInfo planet) => WorldSeed(planet.PlanetId, planet.WorldVariant);

    /// <summary>
    /// The variant that regenerating a planet moves to. It cycles through 1 to
    /// 255 and never returns to the original world, which is chosen explicitly.
    /// </summary>
    public static byte NextWorldVariant(byte worldVariant) =>
        worldVariant == byte.MaxValue ? (byte)1 : (byte)(worldVariant + 1);

    /// <summary>
    /// FNV-1a over the UTF-16 code units of the seed, matching hashSeed in nebula.js.
    /// </summary>
    public static uint HashSeed(string seed)
    {
        var hash = 0x811c9dc5u;
        foreach (var c in seed ?? string.Empty)
        {
            hash ^= c;
            hash *= 0x01000193u;
        }

        return hash == 0 ? 1 : hash;
    }

    public static string PaletteFor(string seed) =>
        PlanetPalettes[HashSeed(seed) % (uint)PlanetPalettes.Length];

    public static string PaletteFor(long planetId, byte worldVariant) =>
        PaletteFor(WorldSeed(planetId, worldVariant));

    /// <summary>
    /// OKLCH hues for each palette as [outer envelope, body, bright core],
    /// matching PALETTES in nebula.js.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, int[]> Hues = new Dictionary<string, int[]>
    {
        ["valour"] = [300, 265, 205],
        ["carina"] = [205, 280, 350],
        ["eagle"] = [230, 190, 85],
        ["rosette"] = [260, 330, 20],
        ["crab"] = [30, 60, 210],
        ["helix"] = [190, 230, 300],
        ["veil"] = [215, 255, 185],
        ["lagoon"] = [330, 10, 60],
        ["orion"] = [250, 320, 35],
        ["tarantula"] = [180, 290, 45]
    };

    /// <summary>
    /// The planet's light: a CSS color in the hue of its sky's bright core. It marks
    /// where you are in a planet, such as the active channel.
    /// </summary>
    public static string PlanetLight(string seed) => $"oklch(0.82 0.1 {Hues[PaletteFor(seed)][2]})";

    public static string PlanetLight(long planetId, byte worldVariant) =>
        PlanetLight(WorldSeed(planetId, worldVariant));

    /// <summary>
    /// The planet's light as a hex color, for places that do not read OKLCH,
    /// such as the theme-color that link previews on other sites use.
    /// </summary>
    public static string PlanetLightHex(long planetId, byte worldVariant) =>
        OklchToHex(0.82, 0.1, Hues[PaletteFor(WorldSeed(planetId, worldVariant))][2]);

    /// <summary>
    /// Converts an OKLCH color to an sRGB hex string, clamping colors outside
    /// the sRGB range.
    /// </summary>
    public static string OklchToHex(double lightness, double chroma, double hueDegrees)
    {
        var hue = hueDegrees * Math.PI / 180;
        var a = chroma * Math.Cos(hue);
        var b = chroma * Math.Sin(hue);

        var l = Math.Pow(lightness + 0.3963377774 * a + 0.2158037573 * b, 3);
        var m = Math.Pow(lightness - 0.1055613458 * a - 0.0638541728 * b, 3);
        var s = Math.Pow(lightness - 0.0894841775 * a - 1.2914855480 * b, 3);

        static int Channel(double linear)
        {
            var encoded = linear <= 0.0031308 ? 12.92 * linear : 1.055 * Math.Pow(linear, 1 / 2.4) - 0.055;
            return (int)Math.Round(Math.Clamp(encoded, 0, 1) * 255);
        }

        var red = Channel(4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s);
        var green = Channel(-1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s);
        var blue = Channel(-0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s);
        return $"#{red:x2}{green:x2}{blue:x2}";
    }
}
