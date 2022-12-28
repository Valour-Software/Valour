using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Valour.Shared.Items.Notifications;

namespace Valour.Database;

[Table("notification_subscriptions")]
public class NotificationSubscription : ISharedNotificationSubscription
{
    [ForeignKey("UserId")]
    public User User { get; set; }

    [Key]
    [Column("id")]
    public long Id {get; set; }

    /// <summary>
    /// The Id of the user this subscription is for
    /// </summary>
    [Column("user_id")]
    public long UserId { get; set; }

    [Column("endpoint")]
    public string Endpoint { get; set; }

    [Column("key")]
    public string Key { get; set; }

    [Column("auth")]
    public string Auth { get; set; }
}
