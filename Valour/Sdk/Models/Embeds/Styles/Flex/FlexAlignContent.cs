using System.Text.Json.Serialization;

namespace Valour.Sdk.Models.Messages.Embeds.Styles.Flex;

public enum AlignContent
{
    FlexStart,
    FlexEnd,
    Center,
    SpaceBetween,
    SpaceAround,
    SpaceEvenly,
    Stretch,
    Start,
    End,
    Baseline,
    FirstBaseline,
    LastBaseline
}

public class FlexAlignContent : StyleBase
{
    private readonly string[] _strings = new string[]
    {
        "align-content: flex-start;",
        "align-content: flex-end;",
        "align-content: center;",
        "align-content: space-between;",
        "align-content: space-around;",
        "align-content: space-evenly;",
        "align-content: stretch;",
        "align-content: start;",
        "align-content: end;",
        "align-content: baseline;",
        "align-content: first baseline;",
        "align-content: last baseline;",
    };

	[JsonPropertyName("v")]
	public AlignContent Value { get; set; }

    public FlexAlignContent(AlignContent value)
    {
        Value = value;
    }

    public override string ToString()
    {
        // Protect from updates or malformed data
        // causing exceptions by just ignoring
        // unknown styles
        if ((int)Value >= _strings.Length)
            return string.Empty;

        return _strings[(int)Value];
    }
}
