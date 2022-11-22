using System.Text.Json.Serialization;

namespace Valour.Api.Items.Messages.Embeds.Styles.Basic;

public class Display : StyleBase
{
    public enum DisplayType
    {
        Inline,
        Block,
        Contents,
        Flex,
        Grid,
        InlineBlock,
        InlineFlex,
        InlineGrid,
        InlineTable,
        ListItem,
        RunIn,
        Table,
        TableCaption,
        TableColumnGroup,
        TableHeaderGroup,
        TableFooterGroup,
        TableRowGroup,
        TableCell,
        TableColumn,
        TableRow,
        None,
        Initial,
        Static
    }

    private readonly string[] _strings = new string[]
    {
        "display: inline;",
        "display: block;",
        "display: contents;",
        "display: flex;",
        "display: grid;",
        "display: inline-block;",
        "display: inline-flex;",
        "display: inline-grid;",
        "display: inline-table;",
        "display: list-item;",
        "display: run-in;",
        "display: table;",
        "display: table-caption;",
        "display: table-column-group;",
        "display: table-header-group;",
        "display: table-footer-group;",
        "display: table-row-group;",
        "display: table-cell;",
        "display: table-column;",
        "display: table-row;",
        "display: none;",
        "display: initial;",
        "display: inherit;"
    };

    [JsonPropertyName("v")]
    public DisplayType Value { get; set; }

    public Display(DisplayType display)
    {
        Value = display;
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
