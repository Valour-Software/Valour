using System.Globalization;

namespace Valour.Client.Utility;

public static class CompactNumberFormatter
{
    private static readonly string[] Suffixes =
        [string.Empty, "K", "M", "B", "T", "Q", "Qi", "Sx", "Sp", "Oc"];

    /// <summary>
    /// Produces a compact balance whose numeric portion is at most three
    /// characters in normal ranges: 999, 13K, 3.1M.
    /// </summary>
    public static string Format(decimal value)
    {
        var suffixIndex = 0;
        var scaled = value;

        while (Math.Abs(scaled) >= 1000 && suffixIndex < Suffixes.Length - 1)
        {
            scaled /= 1000;
            suffixIndex++;
        }

        while (true)
        {
            var decimalPlaces = Math.Abs(scaled) < 10 ? 1 : 0;
            var rounded = Math.Round(scaled, decimalPlaces, MidpointRounding.AwayFromZero);

            if (Math.Abs(rounded) < 1000 || suffixIndex == Suffixes.Length - 1)
            {
                var format = Math.Abs(rounded) < 10 ? "0.#" : "0";
                return rounded.ToString(format, CultureInfo.InvariantCulture) + Suffixes[suffixIndex];
            }

            scaled /= 1000;
            suffixIndex++;
        }
    }
}
