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

public class Border : IStyle
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
        Type = EmbedStyleType.BorderRadius;
    }

    public override string ToString()
    {
        if ((int)Style >= _styleStrings.Length)
            return string.Empty;

        return $"{Thickness} {Color} {_styleStrings[(int)Style]}";
    }
}

public class Borders : IStyle
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

    public Borders(Border border)
    {

    }

    public override string ToString()
    {
        return @$"border-left: {Left};
                  border-right: {Right};
                  border-top: {Top};
                  border-bottom: {Bottom};";
    }

    public Borders(JsonNode Node, Embed embed)
    {
        Type = EmbedStyleType.Borders;
        Id = (string)Node["Id"];
        ItemPlacementType = (EmbedItemPlacementType)(int)Node["ItemPlacementType"];
        if (ItemPlacementType == EmbedItemPlacementType.FreelyBased)
        {
            Width = (int?)Node["Width"];
            Height = (int?)Node["Height"];
        }
        Rows = new();
        Items = new();
        switch (ItemPlacementType)
        {
            case EmbedItemPlacementType.FreelyBased:
                if (Node["Items"] is not null)
                {
                    Items = new();
                    foreach (JsonNode node in Node["Items"].AsArray())
                    {
                        Items.Add(Embed.ConvertNodeToEmbedItem(node, embed));
                    }
                }
                break;
            case EmbedItemPlacementType.RowBased:
                if (Node["Rows"] is not null && Node["Items"] is null)
                {
                    foreach (var rownode in Node["Rows"].AsArray())
                    {
                        EmbedRow rowobject = new();
                        if (rownode["Align"] is not null)
                            rowobject.Align = (EmbedAlignType)(int)rownode["Align"];
                        int i = 0;
                        foreach (JsonNode node in rownode["Items"].AsArray())
                        {
                            rowobject.Items.Add(Embed.ConvertNodeToEmbedItem(node, embed));
                        }
                        Rows.Add(rowobject);
                    }
                }
                break;
        }
    }
}
