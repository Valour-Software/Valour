using Valour.Sdk.Models.Embeds;
using Valour.Sdk.Models.Embeds.Items;

namespace Valour.Tests.Sdk.Embeds;

public class EmbedChartTests
{
    private static Embed BuildChartEmbed()
    {
        return new EmbedBuilder()
            .AddPage("Stats")
                .AddChart(ChartKind.Line, "Revenue")
                    .AddChartSeries("2025", 1, 4, 9, 16)
                    .AddChartSeries("2026", 2, 6, 12, 20).WithSeriesColor("#34c98e")
                    .WithChartLabels("Q1", "Q2", "Q3", "Q4")
                    .WithLegend()
            .Build();
    }

    [Fact]
    public void Builder_ProducesChartShape()
    {
        var chart = Assert.IsType<EmbedChartItem>(BuildChartEmbed().Pages[0].Children[0]);

        Assert.Equal(ChartKind.Line, chart.Kind);
        Assert.Equal("Revenue", chart.Title);
        Assert.Equal(2, chart.Series.Count);
        Assert.Equal(new List<double> { 1, 4, 9, 16 }, chart.Series[0].Values);
        Assert.Equal("#34c98e", chart.Series[1].Color);
        Assert.Null(chart.Series[0].Color);
        Assert.Equal(4, chart.Labels!.Count);
        Assert.True(chart.ShowLegend);
    }

    [Fact]
    public void Chart_RoundTripsThroughWireFormat()
    {
        var json = EmbedParser.Serialize(BuildChartEmbed());
        Assert.Contains("\"$type\":\"chart\"", json);

        var parsed = EmbedParser.TryParse(json);
        Assert.NotNull(parsed);

        var chart = Assert.IsType<EmbedChartItem>(parsed.Pages[0].Children[0]);
        Assert.Equal(2, chart.Series.Count);
        Assert.Equal(20, chart.Series[1].Values[^1]);
        Assert.Equal("Q4", chart.Labels![^1]);
    }

    [Fact]
    public void ChartSeries_WithoutChart_Throws()
    {
        var builder = new EmbedBuilder().AddPage().AddText("x");
        Assert.Throws<InvalidOperationException>(() => builder.AddChartSeries("a", 1));
    }

    [Fact]
    public void Validate_RejectsEmptyChart()
    {
        var embed = new Embed
        {
            Pages = { new EmbedPage { Children = { new EmbedChartItem() } } },
        };

        Assert.False(EmbedParser.Validate(embed).Success);
    }

    [Theory]
    [InlineData("red")]           // not hex
    [InlineData("#12345")]        // wrong length
    [InlineData("#gggggg")]       // not hex digits
    [InlineData("#fff; }<svg")]   // injection attempt
    public void Validate_RejectsNonHexSeriesColors(string color)
    {
        var chart = new EmbedChartItem
        {
            Series = { new EmbedChartSeries { Values = { 1 }, Color = color } },
        };

        Assert.False(EmbedParser.ValidateChart(chart).Success);
    }

    [Fact]
    public void Validate_RejectsNonFiniteValues()
    {
        var chart = new EmbedChartItem
        {
            Series = { new EmbedChartSeries { Values = { 1, double.NaN } } },
        };

        Assert.False(EmbedParser.ValidateChart(chart).Success);
    }

    [Fact]
    public void Validate_EnforcesCaps()
    {
        var tooManySeries = new EmbedChartItem();
        for (var i = 0; i <= EmbedChartItem.MaxSeries; i++)
            tooManySeries.Series.Add(new EmbedChartSeries { Values = { 1 } });
        Assert.False(EmbedParser.ValidateChart(tooManySeries).Success);

        var tooManyPoints = new EmbedChartItem
        {
            Series = { new EmbedChartSeries { Values = Enumerable.Repeat(1.0, EmbedChartItem.MaxPoints + 1).ToList() } },
        };
        Assert.False(EmbedParser.ValidateChart(tooManyPoints).Success);
    }

    [Fact]
    public void Validate_AcceptsValidChart()
    {
        Assert.True(EmbedParser.Validate(BuildChartEmbed()).Success);
    }
}
