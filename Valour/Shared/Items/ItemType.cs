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

