using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Valour.Shared.Models;

namespace Valour.Database;

[Table("user_preferences")]
public class UserPreferences : ISharedUserPreferences
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("error_reporting_state")]
    public ErrorReportingState ErrorReportingState { get; set; }

    [Column("notification_volume")]
    public int NotificationVolume { get; set; } = NotificationPreferences.DefaultNotificationVolume;

    [Column("enabled_notification_sources")]
    public long EnabledNotificationSources { get; set; } = NotificationPreferences.AllNotificationSourcesMask;
}
