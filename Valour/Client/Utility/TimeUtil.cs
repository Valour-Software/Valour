namespace Valour.Client.Utility;

public static class TimeUtil
{
    public static string GetRelativeTime(DateTime utcTime)
    {
        var span = DateTime.UtcNow - utcTime;

        if (span.TotalMinutes < 1) return "Just now";
        if (span.TotalMinutes < 60)
        {
            var mins = (int)span.TotalMinutes;
            return mins == 1 ? "1 min ago" : $"{mins} mins ago";
        }
        if (span.TotalHours < 24)
        {
            var hours = (int)span.TotalHours;
            return hours == 1 ? "1 hour ago" : $"{hours} hours ago";
        }
        if (span.TotalDays < 30)
        {
            var days = (int)span.TotalDays;
            return days == 1 ? "1 day ago" : $"{days} days ago";
        }
        if (span.TotalDays < 365)
        {
            var months = (int)(span.TotalDays / 30);
            return months == 1 ? "1 month ago" : $"{months} months ago";
        }

        var years = (int)(span.TotalDays / 365);
        return years == 1 ? "1 year ago" : $"{years} years ago";
    }
}
