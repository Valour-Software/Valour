using System.Text.Json.Serialization;

namespace Valour.Sdk.Models.Embeds.Items;

public enum ChartKind
{
    Line = 1,
    Bar = 2,
    Pie = 3,
}

/// <summary>
/// A chart rendered client-side as inline SVG — no charting libraries
/// involved. Carries only data; sizing comes from the standard Style
/// properties (defaults to 320x180).
/// </summary>
public class EmbedChartItem : EmbedItem
{
    public const int MaxSeries = 8;
    public const int MaxPoints = 100;
    public const int MaxLabelLength = 32;

    public ChartKind Kind { get; set; } = ChartKind.Line;

    public string? Title { get; set; }

    /// <summary>
    /// Category / x-axis labels. Pie charts use these as slice labels.
    /// </summary>
    public List<string>? Labels { get; set; }

    public List<EmbedChartSeries> Series { get; set; } = new();

    /// <summary>
    /// Shows a legend of series names (or labels, for pie charts).
    /// </summary>
    public bool ShowLegend { get; set; }

    /// <summary>
    /// When true, the y-axis fits tightly to the series' own min/max instead of
    /// always including 0. Useful for charts tracking a large, slow-moving
    /// value where a 0 floor would flatten the visible trend.
    /// </summary>
    public bool ZoomYAxis { get; set; }

    [JsonIgnore]
    public override EmbedItemType ItemType => EmbedItemType.Chart;
}

/// <summary>
/// One data series of a chart. Pie charts read only the first series.
/// </summary>
public class EmbedChartSeries
{
    public string? Name { get; set; }

    public List<double> Values { get; set; } = new();

    /// <summary>
    /// Optional hex color ("#RRGGBB"); a default palette is used otherwise.
    /// </summary>
    public string? Color { get; set; }
}
