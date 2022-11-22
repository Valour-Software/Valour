namespace Valour.Api.Items.Messages.Embeds.Styles.Flex;

public enum Direction
{
    Row,
    RowReverse,
    Column,
    ColumnReverse,
}

public class FlexDirection : StyleBase
{
    private readonly string[] _strings = new string[]
    {
        "flex-direction: row;",
        "flex-direction: row-reverse;",
        "flex-direction: column;",
        "flex-direction: column-reverse;"
    };

    public Direction Type { get; set; }

    public FlexDirection(Direction type)
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
