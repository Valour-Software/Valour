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

public struct Border
{
    public static readonly Border Empty = new Border();

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

public struct Borders : IStyle
{
    public Border Left { get; set; }
    public Border Right { get; set; }
    public Border Top { get; set; }
    public Border Bottom { get; set; }

    public Borders(Border border)
    {
        Left = border;
        Right = border;
        Top = border;
        Bottom = border;
    }

    public Borders(Border left, Border right, Border top, Border bottom)
    {
        Left = left;
        Right = right;
        Top = top;
        Bottom = bottom;
    }

    public override string ToString()
    {
        return @$"border-left: {Left};
                  border-right: {Right};
                  border-top: {Top};
                  border-bottom: {Bottom};";
    }
}
