using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Valour.Shared.Items.Notifications;

public class NotificationSubscription
{
    [Key]
    public ulong Id { get; set; }

    /// <summary>
    /// The Id of the user this subscription is for
    /// </summary>
    public ulong UserId { get; set; }
    public string Endpoint { get; set; }
    public string Not_Key { get; set; }
    public string Auth { get; set; }

}
