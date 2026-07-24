namespace Valour.Server.Models;

public class NotificationContent
{
    public required string Title { get; set; }
    public required string Message { get; set; }
    public string? IconUrl { get; set; }
    public string? Url { get; set; }
    public Guid? NotificationId { get; set; }
    public long? SourceId { get; set; }

    /// <summary>
    /// When the notification was generated (UTC). Carried into push payloads
    /// so OS notification cards show a real time — without it Android renders
    /// a bogus date. Defaults to send time when unset.
    /// </summary>
    public DateTime TimeSent { get; set; }
}
