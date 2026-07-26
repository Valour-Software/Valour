using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Valour.Client.Components.Staff.Dashboard.Charts;

public partial class DashLineChart
{
    private const double XBand = 22;
    private const double PadTop = 6;

    [Parameter] public string Title { get; set; }
    [Parameter] public IReadOnlyList<DashChartSeries> Series { get; set; }
    [Parameter] public bool Area { get; set; }
    [Parameter] public Func<double, string> YFormat { get; set; }
    [Parameter] public string XMode { get; set; } = "auto";
    [Parameter] public double Height { get; set; } = 180;

    private List<DashChartSeries> _series = new();
    private DashChartSeries _timeBase;
    private double _min;
    private double _max;
    private List<double> _ticks = new();
    private double _baselineTick;
    private string _xMode = "day";
    private double _yGutterWidth = 40;
    private double _endGutterWidth = 30;
    private readonly List<(string Text, double Y)> _endLabels = new();
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
            .Take(4)
            .ToList() ?? new List<DashChartSeries>();

        _timeBase = _series.OrderByDescending(s => s.Points.Count).FirstOrDefault();

        if (IsEmpty)
        {
            _hoverIndex = null;
            _endLabels.Clear();
            return;
        }

        var dataMin = double.MaxValue;
        var dataMax = double.MinValue;
        foreach (var series in _series)
        {
            foreach (var point in series.Points)
            {
                dataMin = Math.Min(dataMin, point.Value);
                dataMax = Math.Max(dataMax, point.Value);
            }
        }

        (_min, _max, _ticks) = DashChartMath.NiceScale(dataMin, dataMax);
        _baselineTick = _ticks.Contains(0) ? 0 : _ticks[0];
        _xMode = DashChartMath.ResolveXMode(XMode, _timeBase.Points);
        _yGutterWidth = DashChartMath.GutterWidth(_ticks.Select(Format));

        BuildEndLabels();

        if (_hoverIndex >= PointCount)
            _hoverIndex = null;
    }

    private void BuildEndLabels()
    {
        _endLabels.Clear();
        var kept = new List<double>();

        foreach (var series in _series)
        {
            var y = YPx(series.Points[^1].Value);
            if (kept.Any(k => Math.Abs(k - y) < 12))
                continue;

            kept.Add(y);
            _endLabels.Add((Format(series.Points[^1].Value), y));
        }

        _endGutterWidth = _endLabels.Count == 0
            ? 12
            : Math.Clamp(_endLabels.Max(l => l.Text.Length) * 7 + 14, 30, 72);
    }

    private double YPx(double value)
    {
        var range = _max - _min;
        var fraction = range <= 0 ? 0 : (value - _min) / range;
        return PadTop + (1 - fraction) * (PlotHeight - PadTop);
    }

    private double XFrac(int index) =>
        PointCount <= 1 ? 0.5 : (double)index / (PointCount - 1);

    private string XPct(int index) => Pct(XFrac(index));

    private double BandLeft(int index) =>
        PointCount <= 1 ? 0 : Math.Max(0, (index - 0.5) / (PointCount - 1));

    private double BandRight(int index) =>
        PointCount <= 1 ? 1 : Math.Min(1, (index + 0.5) / (PointCount - 1));

    private string BandLeftPct(int index) => Pct(BandLeft(index));

    private string BandWidthPct(int index) => Pct(BandRight(index) - BandLeft(index));

    private string LinePoints(DashChartSeries series)
    {
        var parts = new List<string>(series.Points.Count);
        for (var i = 0; i < series.Points.Count; i++)
            parts.Add($"{F(XFrac(i) * 1000)},{F(YPx(series.Points[i].Value))}");

        return string.Join(' ', parts);
    }

    private string AreaPath(DashChartSeries series)
    {
        var baselineY = F(YPx(0));
        var parts = new List<string>(series.Points.Count + 3)
        {
            $"M {F(XFrac(0) * 1000)} {baselineY}",
        };

        for (var i = 0; i < series.Points.Count; i++)
            parts.Add($"L {F(XFrac(i) * 1000)} {F(YPx(series.Points[i].Value))}");

        parts.Add($"L {F(XFrac(series.Points.Count - 1) * 1000)} {baselineY}");
        parts.Add("Z");

        return string.Join(' ', parts);
    }

    private IEnumerable<int> XLabelIndices()
    {
        var count = PointCount;
        if (count == 0)
            yield break;

        if (count == 1)
        {
            yield return 0;
            yield break;
        }

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

    private string XAnchorAt(int index) => DashChartMath.XAnchor(XFrac(index));

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

    private bool TooltipFlip => _hoverIndex is { } index && XFrac(index) > 0.6;

    private double TooltipTop
    {
        get
        {
            var rows = _hoverIndex is { } index
                ? _series.Count(s => index < s.Points.Count)
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
