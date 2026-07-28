using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Valour.Client.Components.Staff.Dashboard.Charts;

public partial class DashBarChart
{
    private const double XBand = 22;
    private const double PadTop = 6;

    [Parameter] public string Title { get; set; }
    [Parameter] public IReadOnlyList<DashChartSeries> Series { get; set; }
    [Parameter] public Func<double, string> YFormat { get; set; }
    [Parameter] public string XMode { get; set; } = "day";
    [Parameter] public double Height { get; set; } = 180;

    private readonly record struct Seg(double Y, double H, string Color, bool Rounded);

    private List<DashChartSeries> _series = new();
    private DashChartSeries _timeBase;
    private double _min;
    private double _max;
    private List<double> _ticks = new();
    private string _xMode = "day";
    private double _yGutterWidth = 40;
    private int? _hoverIndex;
    private double _pointerY;
    private bool _showTable;

    private double PlotHeight => Height;
    private double TotalHeight => Height + XBand;
    private int PointCount => _timeBase?.Points.Count ?? 0;
    private bool IsEmpty => PointCount == 0;
    private Func<double, string> Format => YFormat ?? DashFormat.Compact;

    protected override void OnParametersSet()
    {
        _series = Series?
            .Where(s => s?.Points is { Count: > 0 })
            .Take(2)
            .ToList() ?? new List<DashChartSeries>();

        _timeBase = _series.OrderByDescending(s => s.Points.Count).FirstOrDefault();

        if (IsEmpty)
        {
            _hoverIndex = null;
            return;
        }

        double dataMax = 0;
        for (var i = 0; i < PointCount; i++)
            dataMax = Math.Max(dataMax, ValueAt(0, i) + ValueAt(1, i));

        (_min, _max, _ticks) = DashChartMath.NiceScale(0, dataMax);
        _xMode = DashChartMath.ResolveXMode(XMode, _timeBase.Points);
        _yGutterWidth = DashChartMath.GutterWidth(_ticks.Select(Format));

        if (_hoverIndex >= PointCount)
            _hoverIndex = null;
    }

    private double ValueAt(int seriesIndex, int pointIndex)
    {
        if (seriesIndex >= _series.Count)
            return 0;

        var points = _series[seriesIndex].Points;
        return pointIndex < points.Count ? Math.Max(0, points[pointIndex].Value) : 0;
    }

    private double RawAt(int seriesIndex, int pointIndex)
    {
        if (seriesIndex >= _series.Count)
            return 0;

        var points = _series[seriesIndex].Points;
        return pointIndex < points.Count ? points[pointIndex].Value : 0;
    }

    private double TotalAt(int pointIndex) => RawAt(0, pointIndex) + RawAt(1, pointIndex);

    private double YPx(double value)
    {
        var range = _max - _min;
        var fraction = range <= 0 ? 0 : (value - _min) / range;
        return PadTop + (1 - fraction) * (PlotHeight - PadTop);
    }

    private List<Seg> Segments(int index)
    {
        var segs = new List<Seg>(2);
        var bottom = ValueAt(0, index);
        var top = _series.Count > 1 ? ValueAt(1, index) : 0;

        if (bottom > 0 && top > 0)
        {
            var bottomY = YPx(bottom);
            var topY = YPx(bottom + top);
            var topH = bottomY - 2 - topY;
            segs.Add(new Seg(bottomY, PlotHeight - bottomY, _series[0].Color, topH <= 0.5));
            if (topH > 0.5)
                segs.Add(new Seg(topY, topH, _series[1].Color, true));
        }
        else if (bottom > 0)
        {
            var bottomY = YPx(bottom);
            segs.Add(new Seg(bottomY, PlotHeight - bottomY, _series[0].Color, true));
        }
        else if (top > 0)
        {
            var topY = YPx(top);
            segs.Add(new Seg(topY, PlotHeight - topY, _series[1].Color, true));
        }

        return segs;
    }

    private string SegStyle(int index)
    {
        var band = 100.0 / PointCount;
        var center = (index + 0.5) * band;
        var width = $"min(24px, max(2px, {F(band)}% - 4px))";
        return $"x: calc({F(center)}% - {width} / 2); width: {width};";
    }

    private double XCenterFrac(int index) => (index + 0.5) / PointCount;

    private string XCenterPct(int index) => Pct(XCenterFrac(index));

    private IEnumerable<int> XLabelIndices()
    {
        var count = PointCount;
        if (count == 0)
            yield break;

        var step = Math.Max(1, (int)Math.Ceiling(count / 6.0));
        string last = null;
        for (var i = 0; i < count; i += step)
        {
            var label = XLabelAt(i);
            if (label == last)
                continue;

            last = label;
            yield return i;
        }
    }

    private DateTime TimeAt(int index) =>
        _timeBase.Points[Math.Min(index, _timeBase.Points.Count - 1)].Time;

    private string XLabelAt(int index) => DashChartMath.XLabel(TimeAt(index), _xMode);

    private string XAnchorAt(int index) => DashChartMath.XAnchor(XCenterFrac(index));

    private string TableTimeAt(int index) => DashChartMath.TableTime(TimeAt(index), _xMode);

    private List<(string Color, string Name, string Value)> TooltipRows(int index)
    {
        var rows = new List<(string, string, string)>(_series.Count);
        foreach (var series in _series)
        {
            if (index < series.Points.Count)
                rows.Add((series.Color, series.Name, Format(series.Points[index].Value)));
        }

        return rows;
    }

    private bool TooltipFlip => _hoverIndex is { } index && XCenterFrac(index) > 0.6;

    private double TooltipTop
    {
        get
        {
            var rows = _hoverIndex is { } index
                ? _series.Count(s => index < s.Points.Count) + (_series.Count == 2 ? 1 : 0)
                : 0;
            var estimated = 30 + rows * 19;
            var maxTop = Math.Max(4, PlotHeight - estimated);
            return Math.Min(Math.Max(4, _pointerY + 14), maxTop);
        }
    }

    private void OnBandPointer(int index, PointerEventArgs e)
    {
        _hoverIndex = index;
        _pointerY = e.OffsetY;
    }

    private void ClearHover() => _hoverIndex = null;

    private void ToggleTable()
    {
        _showTable = !_showTable;
        _hoverIndex = null;
    }

    private static string F(double value) => DashChartMath.F(value);

    private static string Pct(double fraction) => DashChartMath.Pct(fraction);

    private static MarkupString SvgText(string x, double y, string anchor, string text, bool middle = false) =>
        DashChartMath.SvgText(x, y, anchor, text, middle);
}
