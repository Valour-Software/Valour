using Valour.Shared.Authorization;

namespace Valour.Shared.Models;

public interface ISharedPlanetRole : ISharedPlanetModel<long>, ISortable
{
    public const string BaseRoute = "api/roles";
    public static string GetIdRoute(long id) => $"{BaseRoute}/{id}";
    
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

    public uint GetAuthority() =>
        ISharedPlanetRole.GetAuthority(this);
    
    public bool HasPermission(PlanetPermission perm) =>
        ISharedPlanetRole.HasPermission(this, perm);

    public static uint GetAuthority(ISharedPlanetRole role)
    {
        return uint.MaxValue - role.Position;
    }

    public static bool HasPermission(ISharedPlanetRole role, PlanetPermission perm)
    {
        if (role.IsAdmin)
            return true;
        
        return Permission.HasPermission(role.Permissions, perm);
    }

    uint ISortable.GetSortPosition()
    {
        return Position;
    }
}

