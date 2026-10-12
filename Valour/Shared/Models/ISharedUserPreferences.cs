namespace Valour.Shared.Models;

public interface ISharedUserPreferences : ISharedModel<long>
{
    ErrorReportingState ErrorReportingState { get; set; }
    int NotificationVolume { get; set; }
    long EnabledNotificationSources { get; set; }
    DmPolicy DmPolicy { get; set; }
    DmPolicy CallPolicy { get; set; }
    bool ForceGpuAcceleration { get; set; }

    /// <summary>
    /// Personal per-channel cooldown for activity notifications, in seconds.
    /// Null inherits each planet's cadence default.
    /// </summary>
    int? ActivityCooldownSeconds { get; set; }

    /// <summary>
    /// Whether the iOS app shows NSFW planets, channels and threads. Accounts
    /// registered from the iOS app start with this off, and it can only be
    /// turned on outside the iOS app by an adult with a birth date on file.
    /// </summary>
    bool ShowNsfw { get; set; }
}
