using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.AspNetCore.Components;

namespace Valour.Client.Components.Staff.Dashboard.Charts;

public class DashChartPoint
{
    public DateTime Time { get; set; }
    public double Value { get; set; }
}

public class DashChartSeries
{
    public string Name { get; set; }
    public string Color { get; set; }
    public List<DashChartPoint> Points { get; set; } = new();
}

public static class DashChartPalette
{
    public const string Series1 = "#3987e5";
    public const string Series2 = "#d95926";
    public const string Series3 = "#199e70";
    public const string Series4 = "#c98500";
    public const string Series5 = "#d55181";
    public const string Series6 = "#008300";
    public const string Series7 = "#9085e9";
    public const string Series8 = "#e66767";

    public const string Good = "#0ca30c";
    public const string Warning = "#fab219";
    public const string Serious = "#ec835a";
    public const string Critical = "#d03b3b";

    public const string DeEmphasis = "rgba(255,255,255,0.30)";
}

public static class DashFormat
{
    public static string Compact(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return "0";

        var abs = Math.Abs(value);

        if (abs >= 1_000_000_000)
            return (value / 1_000_000_000).ToString("0.#", CultureInfo.InvariantCulture) + "B";
        if (abs >= 1_000_000)
            return (value / 1_000_000).ToString("0.#", CultureInfo.InvariantCulture) + "M";
        if (abs >= 10_000)
            return (value / 1_000).ToString("0.#", CultureInfo.InvariantCulture) + "K";
        if (abs >= 1_000)
            return value.ToString("#,##0", CultureInfo.InvariantCulture);

        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    public static string Usd(long cents)
    {
        var sign = cents < 0 ? "-$" : "$";
        var dollars = Math.Abs(cents) / 100.0;

        if (dollars >= 1_000_000_000)
            return sign + (dollars / 1_000_000_000).ToString("0.#", CultureInfo.InvariantCulture) + "B";
        if (dollars >= 1_000_000)
            return sign + (dollars / 1_000_000).ToString("0.#", CultureInfo.InvariantCulture) + "M";
        if (dollars >= 1_000)
            return sign + (dollars / 1_000).ToString("0.#", CultureInfo.InvariantCulture) + "K";

        return sign + dollars.ToString("0.00", CultureInfo.InvariantCulture);
    }
}

internal static class DashChartMath
{
    public static string F(double value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture);

    public static string Pct(double fraction) =>
        (fraction * 100).ToString("0.###", CultureInfo.InvariantCulture) + "%";

    public static (double Min, double Max, List<double> Ticks) NiceScale(double dataMin, double dataMax)
    {
        var min = Math.Min(0, dataMin);
        var max = dataMax;
        if (max <= min)
            max = min + 1;

        var step = NiceStep((max - min) / 4);
        var start = Math.Floor(min / step) * step;
        var end = Math.Ceiling(max / step) * step;
        if (end <= start)
            end = start + step;

        var ticks = new List<double>();
        for (var tick = start; tick <= end + step * 0.001; tick += step)
            ticks.Add(Math.Abs(tick) < step * 1e-9 ? 0 : tick);

        return (start, end, ticks);
    }

    private static double NiceStep(double raw)
    {
        if (raw <= 0)
            return 1;

        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var normalized = raw / magnitude;
        var step = normalized <= 1 ? 1
            : normalized <= 2 ? 2
            : normalized <= 2.5 ? 2.5
            : normalized <= 5 ? 5
            : 10;

        return step * magnitude;
    }

    public static string ResolveXMode(string mode, IReadOnlyList<DashChartPoint> points)
    {
        if (mode is "hour" or "day")
            return mode;
        if (points is null || points.Count < 2)
            return "day";

        var span = points[^1].Time - points[0].Time;
        return span <= TimeSpan.FromHours(48) ? "hour" : "day";
    }

    public static string XLabel(DateTime time, string mode) =>
        mode == "hour"
            ? time.ToString("HH:mm", CultureInfo.InvariantCulture)
            : time.ToString("MMM d", CultureInfo.InvariantCulture);

    public static string TableTime(DateTime time, string mode) =>
        mode == "hour"
            ? time.ToString("MMM d HH:mm", CultureInfo.InvariantCulture)
            : time.ToString("MMM d", CultureInfo.InvariantCulture);

    public static string XAnchor(double fraction) =>
        fraction < 0.07 ? "start" : fraction > 0.93 ? "end" : "middle";

    public static double GutterWidth(IEnumerable<string> labels)
    {
        var longest = 0;
        foreach (var label in labels)
            longest = Math.Max(longest, label?.Length ?? 0);

        return Math.Clamp(longest * 6.5 + 10, 28, 64);
    }

    // Razor reserves <text>, so SVG text is emitted as encoded markup.
    // MarkupString content never receives the CSS-isolation scope attribute,
    // so styling must be inline (CSS variables keep it themeable).
    public static MarkupString SvgText(string x, double y, string anchor, string text, bool middle = false)
    {
        var baseline = middle ? " dominant-baseline=\"middle\"" : "";
        var encoded = System.Net.WebUtility.HtmlEncode(text);
        return new MarkupString(
            $"<text x=\"{x}\" y=\"{F(y)}\" text-anchor=\"{anchor}\"{baseline} pointer-events=\"none\" " +
            "style=\"font-size: 11px; font-family: var(--font-family-app, sans-serif); " +
            "fill: var(--font-color-muted, #7a7a7a);\">" + encoded + "</text>");
    }
}
