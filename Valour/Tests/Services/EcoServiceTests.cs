using Valour.Server.Models.Economy;
using Valour.Server.Services;

namespace Valour.Tests.Services;

public class EcoServiceTests
{
    [Fact]
    public void ValidateCurrency_ReturnsFailure_ForNullCurrency()
    {
        var result = EcoService.ValidateCurrency(null);

        Assert.False(result.Success);
        Assert.Contains("null", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateCurrency_ReturnsFailure_ForInvalidName(string? name)
    {
        var currency = CreateValidCurrency();
        currency.Name = name;

        var result = EcoService.ValidateCurrency(currency);

        Assert.False(result.Success);
        Assert.Contains("name", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateCurrency_ReturnsFailure_ForNullPluralName()
    {
        var currency = CreateValidCurrency();
        currency.PluralName = null;

        var result = EcoService.ValidateCurrency(currency);

        Assert.False(result.Success);
        Assert.Contains("plural", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateCurrency_ReturnsFailure_ForNullShortCode()
    {
        var currency = CreateValidCurrency();
        currency.ShortCode = null;

        var result = EcoService.ValidateCurrency(currency);

        Assert.False(result.Success);
        Assert.Contains("shortcode", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateCurrency_ReturnsFailure_ForNullSymbol()
    {
        var currency = CreateValidCurrency();
        currency.Symbol = null;

        var result = EcoService.ValidateCurrency(currency);

        Assert.False(result.Success);
        Assert.Contains("symbol", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateCurrency_ReturnsSuccess_ForValidCurrency()
    {
        var result = EcoService.ValidateCurrency(CreateValidCurrency());

        Assert.True(result.Success, result.Message);
    }

    private static Currency CreateValidCurrency() =>
        new()
        {
            Id = 1,
            PlanetId = 1,
            Name = "Dollar",
            PluralName = "Dollars",
            ShortCode = "USD",
            Symbol = "$",
            DecimalPlaces = 2
        };
}
