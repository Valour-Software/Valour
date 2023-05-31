namespace Valour.Shared.Utilities;

public static class StringExtensions
{
    public static string Truncate(this string value, int length)
        => (value != null && value.Length > length) ? value.Substring(0, length) : value;
}