namespace Valour.Server.Utilities;

public static class ColorHelpers
{
    public static bool ValidateColorCode(string code)
    {
        // Null is valid
        if (code is null)
            return true;
        
        if (!code.StartsWith('#'))
            return false;

        if (code.Length > 7 || code.Length < 3)
            return false;
        
        return true;
    }
}