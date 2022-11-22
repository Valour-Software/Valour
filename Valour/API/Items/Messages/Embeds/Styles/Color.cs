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
