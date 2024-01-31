using System.Text.Json.Serialization;

namespace Valour.Sdk.Models.Messages.Embeds.Styles.Flex;

public enum JustifyContent
{
    FlexStart,
    FlexEnd,
    Center,
    SpaceBetween,
    SpaceAround,
    SpaceEvenly
}

public class FlexJustifyContent : StyleBase
{
    public static readonly FlexJustifyContent FlexStart = new(JustifyContent.FlexStart);
    public static readonly FlexJustifyContent FlexEnd = new(JustifyContent.FlexEnd);
    public static readonly FlexJustifyContent Center = new(JustifyContent.Center);
    public static readonly FlexJustifyContent SpaceBetween = new(JustifyContent.SpaceBetween);
    public static readonly FlexJustifyContent SpaceAround = new(JustifyContent.SpaceAround);
    public static readonly FlexJustifyContent SpaceEvenly = new(JustifyContent.SpaceEvenly);

    private readonly string[] _strings = new string[]
    {
        "justify-content: flex-start;",
        "justify-content: flex-end;",
        "justify-content: center;",
        "justify-content: space-between;",
        "justify-content: space-around;",
        "justify-content: space-evenly;",
    };

	[JsonPropertyName("v")]
	public JustifyContent Value { get; set; }

    public FlexJustifyContent(JustifyContent value)
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
