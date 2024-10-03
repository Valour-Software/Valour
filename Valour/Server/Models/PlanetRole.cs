using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Server.Models;

public class PlanetRole : Item, ISharedPlanetRole
{
    public static PlanetRole DefaultRole = new PlanetRole()
    {
        IsAdmin = false,
        Name = "Default",
        Id = long.MaxValue,
        Position = int.MaxValue,
        PlanetId = 0,
        Color = "#ffffff",
        Permissions = PlanetPermissions.Default,
        ChatPermissions = ChatChannelPermissions.Default,
        CategoryPermissions = Valour.Shared.Authorization.CategoryPermissions.Default,
        VoicePermissions = VoiceChannelPermissions.Default,
        AnyoneCanMention = false,
    };
    
    /// <summary>
    /// True if this is an admin role - meaning that it overrides all permissions
    /// </summary>
    public bool IsAdmin { get; set; }

    /// <summary>
    /// The id of the planet this belongs to
    /// </summary>
    public long PlanetId { get; set; }

    /// <summary>
    /// The position of the role: Lower has more authority
    /// </summary>
    public int Position { get; set; }
    
    /// <summary>
    /// True if this is the default (everyone) role
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// The planet permissions for the role
    /// </summary>
    public long Permissions { get; set; }

    /// <summary>
    /// The chat channel permissions for the role
    /// </summary>
    public long ChatPermissions { get; set; }

    /// <summary>
    /// The category permissions for the role
    /// </summary>
    public long CategoryPermissions { get; set; }

    /// <summary>
    /// The voice channel permissions for the role
    /// </summary>
    public long VoicePermissions { get; set; }

    /// <summary>
    /// The hex color for the role
    /// </summary>
    public string Color { get; set; }

    // Formatting options
    public bool Bold { get; set; }
    public bool Italics { get; set; }
    public string Name { get; set; }
    
    public bool AnyoneCanMention { get; set; }

    public int GetAuthority() =>
        ISharedPlanetRole.GetAuthority(this);

    public bool HasPermission(PlanetPermission perm) =>
        ISharedPlanetRole.HasPermission(this, perm);
}