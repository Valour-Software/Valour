using System.Text.Json.Serialization;

namespace Valour.Api.Items.Messages.Embeds.Styles.Flex;

public class FlexGap : StyleBase
{
	[JsonPropertyName("r")]
	public Size Row { get; set; }

	[JsonPropertyName("c")]
	public Size Column { get; set; }

    public FlexGap(Size row)
    {
        this.Row = row;
        this.Column = Size.Zero;
    }

    public FlexGap(Size row, Size column) 
    { 
        this.Row = row;
        this.Column = column;
    }

    public override string ToString()
    {
        return $"gap: {Row} {Column}";
    }
}
