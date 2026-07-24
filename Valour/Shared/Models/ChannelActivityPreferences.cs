namespace Valour.Shared.Models;

/// <summary>
/// Planet-owner default cadence for channel activity notifications.
/// Controls how often (never whether-per-message) members are notified
/// about non-ping channel activity. See Docs/ChannelActivityNotifications.md.
/// </summary>
public enum ChannelActivityCadence
{
    Off = 0,
    Quiet = 1,
    Standard = 2,
    Lively = 3,
}

/// <summary>
/// Per-user, per-channel override for activity notifications,
/// stored on the user's channel state row.
/// </summary>
public enum ChannelActivityAlerts
{
    /// <summary>Follow the user's global preference / planet cadence</summary>
    Auto = 0,
    /// <summary>Never send activity notifications for this channel (mentions unaffected)</summary>
    Off = 1,
}

public static class ChannelActivityPreferences
{
    /// <summary>Rolling window used to measure channel activity</summary>
    public static readonly TimeSpan ActivityWindow = TimeSpan.FromMinutes(5);

    /// <summary>Minimum messages within the window for a channel to count as active</summary>
    public const int MinWindowMessages = 3;

    /// <summary>Minimum distinct authors within the window for a channel to count as active</summary>
    public const int MinWindowAuthors = 2;

    /// <summary>
    /// A burst after this much silence is framed as a conversation start
    /// ("picking up") rather than ongoing activity
    /// </summary>
    public static readonly TimeSpan ConversationStartGap = TimeSpan.FromMinutes(30);

    /// <summary>Minimum gap between any two activity notifications for one user, across all channels</summary>
    public static readonly TimeSpan GlobalUserGap = TimeSpan.FromSeconds(60);

    /// <summary>At most one candidate evaluation per channel per this period</summary>
    public static readonly TimeSpan EvaluationDebounce = TimeSpan.FromSeconds(60);

    /// <summary>Users who last viewed a channel longer ago than this are never activity-notified for it</summary>
    public static readonly TimeSpan InterestFloor = TimeSpan.FromDays(14);

    /// <summary>Upper bound on users evaluated per activity event</summary>
    public const int MaxCandidatesPerEvaluation = 500;

    /// <summary>Bounds for the user's personal cooldown preference (seconds)</summary>
    public const int MinCooldownSeconds = 60;
    public const int MaxCooldownSeconds = 86_400;

    /// <summary>
    /// Base per-channel cooldown for each planet cadence. A user's personal
    /// cooldown preference, when set, replaces the planet's base entirely.
    /// </summary>
    public static TimeSpan? GetBaseCooldown(ChannelActivityCadence cadence) => cadence switch
    {
        ChannelActivityCadence.Quiet => TimeSpan.FromMinutes(60),
        ChannelActivityCadence.Standard => TimeSpan.FromMinutes(15),
        ChannelActivityCadence.Lively => TimeSpan.FromMinutes(5),
        _ => null, // Off
    };

    /// <summary>
    /// Interest-band multiplier over the base cooldown: the more recently a
    /// user viewed a channel (or if they favorited it), the more often it may
    /// notify them. Returns null when the user is below the interest floor.
    /// </summary>
    public static double? GetInterestMultiplier(DateTime? lastViewedUtc, bool isFavorite, DateTime nowUtc)
    {
        if (isFavorite)
            return 1;

        if (lastViewedUtc is null)
            return null;

        var age = nowUtc - lastViewedUtc.Value;
        if (age < TimeSpan.FromHours(24))
            return 1;
        if (age < TimeSpan.FromDays(7))
            return 2;
        if (age <= InterestFloor)
            return 4;

        return null;
    }
}
