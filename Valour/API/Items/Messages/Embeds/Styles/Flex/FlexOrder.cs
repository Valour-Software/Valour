using System.Text.Json.Serialization;

namespace Valour.Api.Items.Messages.Embeds.Styles.Flex;

public class FlexOrder : StyleBase
{
	[JsonPropertyName("v")]
	public int Value { get; set; }

    public FlexOrder(int value)
    {
        Value = value;
    }

    public override string ToString()
    {
        return $"order: {Value}";
    }
}
