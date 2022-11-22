namespace Valour.Api.Items.Messages.Embeds.Styles.Flex;

public class FlexGap : StyleBase
{
    public Size Row { get; set; }
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
