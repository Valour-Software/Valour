using System.Globalization;

namespace Valour.Sdk.Models.Embeds.Styles;

/// <summary>
/// An RGBA color. Only used at build time; embeds serialize the
/// compiled CSS string.
/// </summary>
public readonly struct Color
{
    public byte Red { get; }
    public byte Green { get; }
    public byte Blue { get; }
    public float Alpha { get; }

    public Color(byte red, byte green, byte blue, float alpha = 1f)
    {
        Red = red;
        Green = green;
        Blue = blue;
        Alpha = alpha;
    }

    /// <summary>
    /// Parses "#RRGGBB", "RRGGBB", "#RRGGBBAA" or "RRGGBBAA".
    /// </summary>
    public Color(string hex)
    {
        if (string.IsNullOrEmpty(hex))
            throw new ArgumentException("Hex color string is required.", nameof(hex));

        var span = hex.AsSpan();
        if (span[0] == '#')
            span = span[1..];

        if (span.Length != 6 && span.Length != 8)
            throw new ArgumentException("Hex color must be in RRGGBB or RRGGBBAA format.", nameof(hex));

        Red = byte.Parse(span[..2], NumberStyles.HexNumber);
        Green = byte.Parse(span[2..4], NumberStyles.HexNumber);
        Blue = byte.Parse(span[4..6], NumberStyles.HexNumber);
        Alpha = span.Length == 8 ? byte.Parse(span[6..8], NumberStyles.HexNumber) / 255f : 1f;
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"rgba({Red}, {Green}, {Blue}, {Alpha})");
}
