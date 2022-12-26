using System.Text.Json.Serialization;

namespace Valour.Api.Items.Messages.Embeds.Styles.Basic;

public class FontSize : StyleBase
{
    [JsonPropertyName("s")]
    public Size Size { get; set; }

    internal FontSize() { }

    public FontSize(Size size)
    {
        Size = size;
    }

    public override string ToString()
    {
        return $"font-size: {Size};";
    }
}
