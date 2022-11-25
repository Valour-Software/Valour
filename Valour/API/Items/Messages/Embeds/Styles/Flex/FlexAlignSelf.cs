using System.Text.Json.Serialization;

namespace Valour.Api.Items.Messages.Embeds.Styles.Flex;

public enum AlignSelf
{
    Auto,
    FlexStart,
    FlexEnd,
    Center,
    Baseline,
    Stretch
}

public class FlexAlignSelf : StyleBase
{
    private readonly string[] _strings = new string[]
    {
        "align-self: auto;",
        "align-self: flex-start;",
        "align-self: flex-end;",
        "align-self: center;",
        "align-self: baseline;",
        "align-self: stretch;",
    };

	[JsonPropertyName("t")]
	public AlignSelf Type { get; set; }

    public FlexAlignSelf(AlignSelf type)
    {
        Type = type;
    }

    public override string ToString()
    {
        // Protect from updates or malformed data
        // causing exceptions by just ignoring
        // unknown styles
        if ((int)Type >= _strings.Length)
            return string.Empty;

        return _strings[(int)Type];
    }
}
