namespace Valour.Api.Items.Messages.Embeds.Styles.Flex;

public enum Wrap
{
    NoWrap,
    Wrap,
    WrapReverse,
}

public struct FlexWrap : IStyle
{
    private readonly string[] _strings = new string[]
    {
        "flex-wrap: nowrap;",
        "flex-wrap: wrap;",
        "flex-wrap: wrap-reverse;",
    };

    public Wrap Type { get; set; }

    public FlexWrap(Wrap type)
    {
        this.Type = type;
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
