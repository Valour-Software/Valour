using System.Text.Json.Serialization;

namespace Valour.Api.Models.Messages.Embeds.Styles.Basic;

public class Margin : StyleBase
{
    [JsonPropertyName("l")]
    public Size Left { get; set; }

    [JsonPropertyName("r")]
    public Size Right { get; set; }

    [JsonPropertyName("t")]
    public Size Top { get; set; }

    [JsonPropertyName("b")]
    public Size Bottom { get; set; }

    public Margin() { }

    public Margin(Size size)
    {
        Left = size;
        Right = size;
        Top = size;
        Bottom = size;
    }

    public Margin(Size left = null, Size right = null, Size top = null, Size bottom = null)
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
            s += $"margin-left: {Left};";
        if (Right is not null)
            s += $"margin-right: {Right};";
        if (Top is not null)
            s += $"margin-top: {Top};";
        if (Bottom is not null)
            s += $"margin-bottom: {Bottom};";
        return s;
    }
}
