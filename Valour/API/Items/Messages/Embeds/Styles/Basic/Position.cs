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

    public Position(Size left, Size right, Size top, Size bottom)
    {
        Left = left;
        Right = right;
        Top = top;
        Bottom = bottom;
    }

    public override string ToString()
    {
        return @$"left: {Left};
                  right: {Right};
                  top: {Top};
                  bottom: {Bottom};";
    }
}
