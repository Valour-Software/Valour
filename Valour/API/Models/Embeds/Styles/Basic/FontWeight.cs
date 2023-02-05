using System.Text.Json.Serialization;

namespace Valour.Api.Models.Messages.Embeds.Styles.Basic;

public class FontWeight : StyleBase
{
	public static readonly FontWeight Bold = new(700);

	[JsonPropertyName("w")]
    public int Weight { get; set; }

    public FontWeight(int weight)
    {
        Weight = weight;
    }

    public override string ToString()
    {
        return $"font-weight: {Weight};";
    }
}
