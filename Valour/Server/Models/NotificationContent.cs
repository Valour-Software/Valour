namespace Valour.Server.Models;

public class NotificationContent
{
    public required string Title { get; set; }
    public required string Message { get; set; }
    public string? IconUrl { get; set; }
    public string? Url { get; set; }
    public Guid? NotificationId { get; set; }
    public long? SourceId { get; set; }
}
