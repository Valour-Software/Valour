using System.Text.Json.Serialization;

namespace Valour.Api.Models.Messages.Embeds.Styles.Flex;

public class FlexShrink : StyleBase
{
	[JsonPropertyName("v")]
	public int Value { get; set; }

    public FlexShrink(int value)
    {
        Value = value;
    }

    public override string ToString()
    {
        return $"flex-shrink: {Value};";
    }
}
