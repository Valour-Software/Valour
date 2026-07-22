using System.Globalization;
using Valour.Client.Utility;

namespace Valour.Tests.Client;

public class CompactNumberFormatterTests
{
    [Theory]
    [InlineData("0", "0")]
    [InlineData("3.14", "3.1")]
    [InlineData("999.4", "999")]
    [InlineData("999.5", "1K")]
    [InlineData("3100", "3.1K")]
    [InlineData("12800", "13K")]
    [InlineData("999499", "999K")]
    [InlineData("999500", "1M")]
    [InlineData("2450000", "2.5M")]
    [InlineData("79228162514264337593543950335", "79Oc")]
    public void Format_KeepsBalancesCompact(string input, string expected)
    {
        Assert.Equal(expected, CompactNumberFormatter.Format(decimal.Parse(input, CultureInfo.InvariantCulture)));
    }
}
