using System.Text.Json.Serialization;

namespace Valour.Shared.Items.Notifications;

public interface ISharedNotificationSubscription
{
    /// <summary>
    /// The Id of this subscription
    /// </summary>
    [JsonPropertyName("Id")]
    public ulong Id { get; set; }

    /// <summary>
    /// The Id of the user this subscription is for
    /// </summary>
    [JsonPropertyName("User_Id")]
    public ulong User_Id { get; set; }

    [JsonPropertyName("Endpoint")]
    public string Endpoint { get; set; }

    [JsonPropertyName("Not_Key")]
    public string Not_Key { get; set; }

    [JsonPropertyName("Auth")]
    public string Auth { get; set; }
}
