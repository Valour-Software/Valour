using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Valour.Shared.Items;

/// <summary>
/// Enum for all Valour item types
/// </summary>
public enum ItemType
{
    ChatChannel,
    Category,
    Planet,
    Invite,
    Member,
    User,
    PermissionsNode,
    PlanetRole,
    PlanetRoleMember,
    NotificationSubscription,
    OauthApp,
    AuthToken,
    PlanetBan,
    Referral,
    PlanetMessage
}

/// <summary>
/// Common class for Valour API items
/// </summary>
public interface ISharedItem
{
    [JsonInclude]
    [JsonPropertyName("Id")]
    public ulong Id { get; set; }

    [NotMapped]
    [JsonInclude]
    [JsonPropertyName("ItemType")]
    public abstract ItemType ItemType { get; }
}

