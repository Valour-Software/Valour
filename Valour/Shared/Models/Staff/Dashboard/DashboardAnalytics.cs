namespace Valour.Shared.Models.Staff;

/// <summary>
/// Historical time series for the staff dashboard's trend charts.
/// </summary>
public class DashboardAnalytics
{
    /// <summary>
    /// The day span the daily series cover.
    /// </summary>
    public int Days { get; set; }

    /// <summary>
    /// Distinct active users per day.
    /// </summary>
    public List<DashboardTimePoint> DailyActiveUsers { get; set; }

    /// <summary>
    /// Rolling 30-day distinct active users, one point per day.
    /// </summary>
    public List<DashboardTimePoint> MonthlyActiveUsers { get; set; }

    /// <summary>
    /// New accounts per day.
    /// </summary>
    public List<DashboardTimePoint> Signups { get; set; }

    /// <summary>
    /// Messages per day, summed from the minute-level platform stats.
    /// </summary>
    public List<DashboardTimePoint> MessagesPerDay { get; set; }

    /// <summary>
    /// Messages per hour over the last 48 hours, for the short-window
    /// frequency chart and tile sparklines.
    /// </summary>
    public List<DashboardTimePoint> MessagesPerHour { get; set; }

    /// <summary>
    /// Real-money revenue per day, split by source.
    /// </summary>
    public List<DashboardRevenuePoint> RevenuePerDay { get; set; }
}

public class DashboardTimePoint
{
    public DateTime TimeUtc { get; set; }
    public long Value { get; set; }
}

public class DashboardRevenuePoint
{
    public DateTime TimeUtc { get; set; }

    /// <summary>
    /// One-time credit-pack purchases, USD cents.
    /// </summary>
    public long OneTimeCents { get; set; }

    /// <summary>
    /// Subscription charges, USD cents.
    /// </summary>
    public long SubscriptionCents { get; set; }
}
