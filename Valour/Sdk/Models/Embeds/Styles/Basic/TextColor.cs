using System.Text.Json.Serialization;

namespace Valour.Api.Models.Messages.Embeds.Styles.Basic;

public class TextColor : StyleBase
{
    [JsonPropertyName("c")]
    public Color Color { get; set; }

    [JsonConstructor]
    public TextColor(Color color)
    {
        Color = color;
    }

	/// <summary>
	/// </summary>
	/// <param name="hex">Must be in #xxxxxx or xxxxxx format!</param>
	public TextColor(string hex)
    {
        Color = new Color(hex);
    }

    public override string ToString()
    {
        return $"color: {Color};";
    }
}
