namespace Valour.Api.Items.Messages.Embeds.Styles.Flex;

public enum JustifyContent
{
    FlexStart,
    FlexEnd,
    Center,
    SpaceBetween,
    SpaceAround,
    SpaceEvenly
}

public struct FlexJustifyContent : IStyle
{

    private readonly string[] _strings = new string[]
    {
        "justify-content: flex-start;",
        "justify-content: flex-end;",
        "justify-content: center;",
        "justify-content: space-between;",
        "justify-content: space-around;",
        "justify-content: space-evenly;",
    };

    public JustifyContent Value { get; set; }

    public FlexJustifyContent(JustifyContent value)
    {
        this.Value = value;
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
