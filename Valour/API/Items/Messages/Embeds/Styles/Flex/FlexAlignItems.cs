using System.Text.Json.Serialization;

namespace Valour.Api.Items.Messages.Embeds.Styles.Flex;

public enum AlignItem
{
    Stretch,
    FlexStart,
    FlexEnd,
    Center,
    Baseline,
    FirstBaseline,
    LastBaseline,
    Start,
    End,
    SelfStart,
    SelfEnd,
}

public class FlexAlignItems : StyleBase
{
	public static readonly FlexAlignItems Stretch = new(AlignItem.Stretch);
	public static readonly FlexAlignItems FlexStart = new(AlignItem.FlexStart);
	public static readonly FlexAlignItems FlexEnd = new(AlignItem.FlexEnd);
	public static readonly FlexAlignItems Center = new(AlignItem.Center);
	public static readonly FlexAlignItems Baseline = new(AlignItem.Baseline);
	public static readonly FlexAlignItems FirstBaseline = new(AlignItem.FirstBaseline);
	public static readonly FlexAlignItems LastBaseline = new(AlignItem.LastBaseline);
	public static readonly FlexAlignItems Start = new(AlignItem.Start);
	public static readonly FlexAlignItems End = new(AlignItem.End);
	public static readonly FlexAlignItems SelfStart = new(AlignItem.SelfStart);
	public static readonly FlexAlignItems SelfEnd = new(AlignItem.SelfEnd);

	private readonly string[] _strings = new string[]
    {
        "align-items: stretch;",
        "align-items: flex-start;",
        "align-items: flex-end;",
        "align-items: center;",
        "align-items: baseline;",
        "align-items: first baseline;",
        "align-items: last baseline;",
        "align-items: start;",
        "align-items: end;",
        "align-items: self-start;",
        "align-items: self-end;",
    };

	[JsonPropertyName("v")]
	public AlignItem Value { get; set; }

    public FlexAlignItems(AlignItem value)
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
