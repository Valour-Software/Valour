namespace Valour.Shared.Models;

public interface ISharedUserPreferences : ISharedModel<long>
{
    ErrorReportingState ErrorReportingState { get; set; }
    int NotificationVolume { get; set; }
    long EnabledNotificationSources { get; set; }
    DmPolicy DmPolicy { get; set; }
    bool ForceGpuAcceleration { get; set; }

    /// <summary>
    /// Personal per-channel cooldown for activity notifications, in seconds.
    /// Null inherits each planet's cadence default.
    /// </summary>
    int? ActivityCooldownSeconds { get; set; }
}
