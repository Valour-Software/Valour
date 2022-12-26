using System.Text.Json.Serialization;

namespace Valour.Api.Items.Messages.Embeds.Styles.Basic;

public class Position : StyleBase
{
    [JsonPropertyName("l")]
    public Size Left {  get; set; }

    [JsonPropertyName("r")]
    public Size Right { get; set; }

    [JsonPropertyName("t")]
    public Size Top {  get; set; }

    [JsonPropertyName("b")]
    public Size Bottom {  get; set; }

    internal Position() { }

    public Position(Size left = null, Size right = null, Size top = null, Size bottom = null)
    {
        Left = left;
        Right = right;
        Top = top;
        Bottom = bottom;
    }

    public override string ToString()
    {
        var s = "position: absolute;";
        if (Left is not null)
            s += $"left: {Left};";
        if (Right is not null)
            s += $"right: {Right};";
        if (Top is not null)
            s += $"top: {Top};";
        if (Bottom is not null)
            s += $"bottom: {Bottom};";
        return s;
    }
}
