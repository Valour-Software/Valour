using System.Text.RegularExpressions;

namespace Valour.Server.Utilities;

public static partial class ColorHelpers
{
    /// <summary>
    /// #RGB, #RGBA, #RRGGBB, or #RRGGBBAA. These values are emitted into a
    /// style block for shared themes, so only actual hex colors are accepted -
    /// the previous check allowed any characters as long as the length fit.
    /// </summary>
    [GeneratedRegex("^#([0-9a-fA-F]{3,4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$")]
    private static partial Regex HexColorRegex();

    public static bool ValidateColorCode(string code)
    {
        // Null is valid
        if (code is null)
            return true;

        return HexColorRegex().IsMatch(code);
    }
}
