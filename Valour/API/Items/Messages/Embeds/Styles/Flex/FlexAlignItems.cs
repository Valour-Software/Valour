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

    public AlignItem Value { get; set; }

    public FlexAlignItems(AlignItem value)
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
