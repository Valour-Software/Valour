using System.Text.Json.Serialization;

namespace Valour.Api.Items.Messages.Embeds.Styles;

public class Color
{
    [JsonPropertyName("r")]
    public byte Red { get; set; }

    [JsonPropertyName("g")]
    public byte Green { get; set; }

    [JsonPropertyName("b")]
    public byte Blue { get; set; }

    [JsonPropertyName("a")]
    public float Alpha { get; set; }

    /// <summary>
    /// </summary>
    /// <param name="hex">Must be in xxxxxx format!</param>
	public Color(string hex)
    {
        Red = byte.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
		Green = byte.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
		Blue = byte.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);
        Alpha = 1f;
	}

    [JsonConstructor]
	public Color(byte red, byte green, byte blue, float alpha = 1f)
    {
        Red = red;
        Green = green;
        Blue = blue;
        Alpha = alpha;
    }

    public override string ToString()
    {
        return $"rgba({Red}, {Green}, {Blue}, {Alpha})";
    }
}
