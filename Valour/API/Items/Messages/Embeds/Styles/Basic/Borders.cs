using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
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
    [JsonPropertyName("t")]
    public Size Thickness { get; set; }

    [JsonPropertyName("c")]
    public Color Color { get; set; }

    [JsonPropertyName("s")]
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

	internal Borders() { }

    public Borders(Border left = null, Border right = null, Border top = null, Border bottom = null)
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
        if (Only is not null)
            return $@"border: {Only};";
		var s = "";
		if (Left is not null)
			s += $"border-left: {Left};";
		if (Right is not null)
			s += $"border-right: {Right};";
		if (Top is not null)
			s += $"border-top: {Top};";
		if (Bottom is not null)
			s += $"border-bottom: {Bottom};";
		return s;
    }
}
