namespace Valour.Shared.Models;

/// <summary>
/// Wire model for planet community events — used for both API responses
/// (with RSVP info populated) and create/update requests
/// </summary>
public class PlanetEventData
{
    public const int MaxTitleLength = 128;
    public const int MaxDescriptionLength = 2048;
    public const int MaxLocationLength = 256;

    public long Id { get; set; }
    public long PlanetId { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }

    /// <summary>
    /// Freeform: a channel name, a URL, or a physical place
    /// </summary>
    public string Location { get; set; }

    public DateTime StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }

    // Response-only fields
    public int GoingCount { get; set; }
    public bool SelfGoing { get; set; }

    /// <summary>
    /// How many minutes before start this user wants a reminder
    /// notification; null when no reminder is set (response-only)
    /// </summary>
    public int? SelfReminderMinutes { get; set; }
}
