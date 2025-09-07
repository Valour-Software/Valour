using Valour.Shared.Models;

namespace Valour.Sdk.Models;

/// <summary>
/// Extended token information including app details
/// </summary>
public class TokenInfo
{
    /// <summary>
    /// The token ID
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    /// The app that created this token
    /// </summary>
    public string AppId { get; set; }

    /// <summary>
    /// The app name (if available)
    /// </summary>
    public string AppName { get; set; }

    /// <summary>
    /// The app icon URL (if available)
    /// </summary>
    public string AppIconUrl { get; set; }

    /// <summary>
    /// The user this token belongs to
    /// </summary>
    public long UserId { get; set; }

    /// <summary>
    /// The scope of permissions this token has
    /// </summary>
    public long Scope { get; set; }

    /// <summary>
    /// When this token was created
    /// </summary>
    public DateTime TimeCreated { get; set; }

    /// <summary>
    /// When this token expires
    /// </summary>
    public DateTime TimeExpires { get; set; }

    /// <summary>
    /// The IP address this token was issued from
    /// </summary>
    public string IssuedAddress { get; set; }

    /// <summary>
    /// Whether this token is expired
    /// </summary>
    public bool IsExpired => TimeExpires < DateTime.UtcNow;

    /// <summary>
    /// Whether this token expires soon (within 24 hours)
    /// </summary>
    public bool ExpiresSoon => TimeExpires < DateTime.UtcNow.AddHours(24) && !IsExpired;

    /// <summary>
    /// Time remaining until expiration
    /// </summary>
    public TimeSpan TimeRemaining => TimeExpires - DateTime.UtcNow;

    /// <summary>
    /// Formatted time remaining string
    /// </summary>
    public string TimeRemainingString
    {
        get
        {
            if (IsExpired)
                return "Expired";

            var remaining = TimeRemaining;
            if (remaining.TotalDays >= 1)
                return $"{(int)remaining.TotalDays} day{(remaining.TotalDays >= 2 ? "s" : "")}";
            else if (remaining.TotalHours >= 1)
                return $"{(int)remaining.TotalHours} hour{(remaining.TotalHours >= 2 ? "s" : "")}";
            else
                return $"{(int)remaining.TotalMinutes} minute{(remaining.TotalMinutes >= 2 ? "s" : "")}";
        }
    }
}
