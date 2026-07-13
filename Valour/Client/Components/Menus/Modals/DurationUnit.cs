namespace Valour.Client.Components.Menus.Modals;

public enum DurationUnit
{
    Seconds,
    Minutes,
    Hours,
    Days,
    Weeks,
    Months,
    Years,
    Permanent
}

public static class DurationUnitExtensions
{
    public static DateTime AddTo(this DurationUnit unit, DateTime date, int amount) =>
        unit switch
        {
            DurationUnit.Seconds => date.AddSeconds(amount),
            DurationUnit.Minutes => date.AddMinutes(amount),
            DurationUnit.Hours => date.AddHours(amount),
            DurationUnit.Days => date.AddDays(amount),
            DurationUnit.Weeks => date.AddDays(amount * 7),
            DurationUnit.Months => date.AddMonths(amount),
            DurationUnit.Years => date.AddYears(amount),
            _ => date
        };

    public static string ToCommandString(this DurationUnit unit, int amount)
    {
        var length = unit switch
        {
            DurationUnit.Seconds => "s",
            DurationUnit.Minutes => "m",
            DurationUnit.Hours => "h",
            DurationUnit.Days => "d",
            DurationUnit.Weeks => "w",
            DurationUnit.Months => "M",
            DurationUnit.Years => "y",
            _ => "m"
        };

        return length;
    }

    // Permanent when explicitly picked from the dropdown, or when the amount is 0/blank.
    public static bool IsPermanent(this DurationUnit unit, int amount) =>
        unit == DurationUnit.Permanent || amount <= 0;
}
