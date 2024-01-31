using System.Text.Json.Serialization;

namespace Valour.Sdk.Models.Messages.Embeds.Styles.Basic;

public class FontSize : StyleBase
{
    [JsonPropertyName("s")]
    public Size Size { get; set; }

    public FontSize(Size size)
    {
        Size = size;
    }

    public override string ToString()
    {
        return $"font-size: {Size};";
    }
}
