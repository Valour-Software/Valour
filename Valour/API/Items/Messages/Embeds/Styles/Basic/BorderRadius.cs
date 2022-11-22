using System.Text.Json.Serialization;

namespace Valour.Api.Items.Messages.Embeds.Styles.Basic;

public class BorderRadius : StyleBase
{
    [JsonPropertyName("tl")]
    public Size TopLeft { get; set; }

    [JsonPropertyName("tr")]
    public Size TopRight { get; set; }

    [JsonPropertyName("bl")]
    public Size BottomLeft { get; set; }

    [JsonPropertyName("br")]
    public Size BottomRight { get; set; }

    [JsonPropertyName("o")]
    public Size? Only { get; set; }

    public BorderRadius(Size size)
    {
        Only = size;
    }

    public BorderRadius(Size topLeft, Size topRight, Size bottomLeft, Size bottomRight)
    {
        TopLeft = topLeft;
        TopRight = topRight;
        BottomLeft = bottomLeft;
        BottomRight = bottomRight;
    }

    public override string ToString()
    {
        if (Only is null)
            return $"border-radius: {TopLeft} {TopRight} {BottomLeft} {BottomRight};";
        else
            return $"border-radius: {Only};";
    }
}
