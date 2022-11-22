using System.Text.Json.Serialization;

namespace Valour.Api.Items.Messages.Embeds.Styles.Basic;

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

    public Margin(Size size)
    {
        Left = size;
        Right = size;
        Top = size;
        Bottom = size;
    }

    public Margin(Size left, Size right, Size top, Size bottom)
    {
        Left = left;
        Right = right;
        Top = top;
        Bottom = bottom;
    }

    public override string ToString()
    {
        return @$"margin-left: {Left};
                  margin-right: {Right};
                  margin-top: {Top};
                  margin-bottom: {Bottom};";
    }
}
