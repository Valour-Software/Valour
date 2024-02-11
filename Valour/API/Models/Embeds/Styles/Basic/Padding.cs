using System.Text.Json.Serialization;

namespace Valour.Api.Models.Messages.Embeds.Styles.Basic;

public class Padding : StyleBase
{
    [JsonPropertyName("l")]
    public Size Left { get; set; }

    [JsonPropertyName("r")]
    public Size Right { get; set; }

    [JsonPropertyName("t")]
    public Size Top { get; set; }

    [JsonPropertyName("b")]
    public Size Bottom { get; set; }

    public Padding() {}

    public Padding(Size size)
    {
        Left = size;
        Right = size;
        Top = size;
        Bottom = size;
    }

    public Padding(Size left = null, Size right = null, Size top = null, Size bottom = null)
    {
        Left = left;
        Right = right;
        Top = top;
        Bottom = bottom;
    }

    public override string ToString()
    {
        var s = "";
        if (Left is not null)
            s += $"padding-left: {Left};";
        if (Right is not null)
            s += $"padding-right: {Right};";
        if (Top is not null)
            s += $"padding-top: {Top};";
        if (Bottom is not null)
            s += $"padding-bottom: {Bottom};";
        return s;
	}
}
