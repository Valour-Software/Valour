using System.Text.Json.Serialization;

namespace Valour.Api.Items.Messages.Embeds.Styles.Flex;

public class FlexGrow : StyleBase
{
	[JsonPropertyName("v")]
	public int Value { get; set; }

    public FlexGrow(int value)
    {
        Value = value;
    }

    public override string ToString()
    {
        return $"flex-grow: {Value};";
    }
}
