using System.Text.Json.Serialization;

namespace Valour.Api.Models.Messages.Embeds.Styles.Basic;

public class BackgroundColor : StyleBase
{
    [JsonPropertyName("c")]
    public Color Color { get; set; }

	[JsonConstructor]
	public BackgroundColor(Color color)
    {
        Color = color;
    }

	/// <summary>
	/// </summary>
	/// <param name="hex">Must be in #xxxxxx or xxxxxx format!</param>
	public BackgroundColor(string hex)
	{
		Color = new Color(hex);
	}

	public override string ToString()
    {
        return $"background-color: {Color};";
    }
}
