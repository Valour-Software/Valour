using System.Text.Json.Nodes;
using Valour.Api.Items.Messages.Embeds.Items;

namespace Valour.Api.Items.Messages.Embeds.Styles.Basic;

public enum BorderStyle
{
    Solid,
    Dotted,
    Dashed,
    Double,
    Groove,
    Ridge,
    Inset,
    Outset,
    None,
    Hidden
}

public class Border : StyleBase
{
    public Size Thickness { get; set; }
    public Color Color { get; set; }
    public BorderStyle Style { get; set; }

    private readonly string[] _styleStrings = new string[]
    {
        "solid",
        "dotted",
        "dashed",
        "double",
        "groove",
        "ridge",
        "inset",
        "outset",
        "none",
        "hidden"
    };

    public Border(Size width, Color color, BorderStyle style)
    {
        Thickness = width;
        Color = color;
        Style = style;
    }

    public override string ToString()
    {
        if ((int)Style >= _styleStrings.Length)
            return string.Empty;

        return $"{Thickness} {Color} {_styleStrings[(int)Style]}";
    }
}

public class Borders : StyleBase
{
    public Border Left { get; set; }
    public Border Right { get; set; }
    public Border Top { get; set; }
    public Border Bottom { get; set; }

    public Border Only { get; set; }

    public Borders(Border left, Border right, Border top, Border bottom)
    {
        Left = left;
        Right = right;
        Top = top;
        Bottom = bottom;
    }

    public Borders(Border border)
    {
        Only = border;
    }

    public override string ToString()
    {
        return @$"border-left: {Left};
                  border-right: {Right};
                  border-top: {Top};
                  border-bottom: {Bottom};";
    }
}
