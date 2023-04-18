using System.Drawing;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Server.Models;

public class PlanetRole : Item, ISharedPlanetRole
{
    public static PlanetRole DefaultRole = new PlanetRole()
    {
        Name = "Default",
        Id = long.MaxValue,
        Position = int.MaxValue,
        PlanetId = 0,
        Red = 255,
        Green = 255,
        Blue = 255,
        Permissions = PlanetPermissions.Default,
        ChatPermissions = ChatChannelPermissions.Default,
        CategoryPermissions = Valour.Shared.Authorization.CategoryPermissions.Default,
        VoicePermissions = VoiceChannelPermissions.Default
    };

    /// <summary>
    /// The id of the planet this belongs to
    /// </summary>
    public long PlanetId { get; set; }

    /// <summary>
    /// The position of the role: Lower has more authority
    /// </summary>
    public int Position { get; set; }

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

    // RGB Components for role color
    public byte Red { get; set; }
    public byte Green { get; set; }
    public byte Blue { get; set; }

    // Formatting options
    public bool Bold { get; set; }
    public bool Italics { get; set; }
    
    public string Name { get; set; }

    public int GetAuthority() =>
        ISharedPlanetRole.GetAuthority(this);

    public Color GetColor() =>
        ISharedPlanetRole.GetColor(this);

    public string GetColorHex() =>
        ISharedPlanetRole.GetColorHex(this);

    public bool HasPermission(PlanetPermission perm) =>
        ISharedPlanetRole.HasPermission(this, perm);
}