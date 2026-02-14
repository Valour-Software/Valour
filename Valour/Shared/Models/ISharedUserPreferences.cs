namespace Valour.Shared.Models;

public interface ISharedUserPreferences : ISharedModel<long>
{
    ErrorReportingState ErrorReportingState { get; set; }
    int NotificationVolume { get; set; }
    long EnabledNotificationSources { get; set; }
}
