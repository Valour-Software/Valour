using Valour.Shared.Authorization;

namespace Valour.Shared.Models;

public interface ISharedPlanetRole : ISharedPlanetModel<long>, ISortable
{
    public static string GetBaseRoute(long planetId) => $"api/planet/{planetId}/roles";
    public static string GetIdRoute(long planetId, long id) => $"{GetBaseRoute(planetId)}/{id}";
    
    /// <summary>
    /// The index of the role in the membership flags.
    /// Ex: 5 would be the 5th bit in the membership flags
    /// </summary>
    public int FlagBitIndex { get; set; }
    
    /// <summary>
    /// True if this is an admin role - meaning that it overrides all permissions
    /// </summary>
    bool IsAdmin { get; set; }
    
    /// <summary>
    /// True if this is the default (everyone) role
    /// </summary>
    bool IsDefault { get; set; }

    /// <summary>
    /// The planet permissions for the role
    /// </summary>
    long Permissions { get; set; }

    /// <summary>
    /// The chat channel permissions for the role
    /// </summary>
    long ChatPermissions { get; set; }

    /// <summary>
    /// The category permissions for the role
    /// </summary>
    long CategoryPermissions { get; set; }

    /// <summary>
    /// The voice channel permissions for the role
    /// </summary>
    long VoicePermissions { get; set; }
    
    /// <summary>
    /// The name of the role
    /// </summary>
    string Name { get; set; }

    /// <summary>
    /// The hex color of the role
    /// </summary>
    string Color { get; set; }

    // Formatting options
    bool Bold { get; set; }

    bool Italics { get; set; }
    
    /// <summary>
    /// True if the role can be mentioned by non-admins
    /// </summary>
    bool AnyoneCanMention { get; set; }
    
    /// <summary>
    /// The position of the role: Lower has more authority
    /// </summary>
    uint Position { get; set; }

    uint ISortable.GetSortPosition()
    {
        return (uint)Position;
    }
}

public static class SharedPlanetRoleExtensions
{
    extension(ISharedPlanetRole role)
    {
        /// <summary>
        /// The role's authority. Lower positions have more authority, and the
        /// default role (the highest position) has none.
        /// </summary>
        public uint GetAuthority() =>
            role.Position == uint.MaxValue ? 0 : uint.MaxValue - role.Position - 1;

        /// <summary>
        /// Whether the role grants the permission. Admin roles grant everything.
        /// </summary>
        public bool HasPermission(PlanetPermission perm) =>
            role.IsAdmin || Permission.HasPermission(role.Permissions, perm);

        /// <summary>
        /// Display Role is a cosmetic flag, not an authorization grant, so the
        /// admin bypass in HasPermission does not apply (#1510)
        /// </summary>
        public bool HasDisplayRole =>
            (role.Permissions & PlanetPermissions.DisplayRole.Value) == PlanetPermissions.DisplayRole.Value;
    }
}
