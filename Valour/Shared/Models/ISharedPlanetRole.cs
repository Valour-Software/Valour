using System.ComponentModel.DataAnnotations.Schema;
using System.Drawing;
using Valour.Shared.Authorization;


namespace Valour.Shared.Models;

public interface ISharedPlanetRole : ISharedPlanetItem
{
    /// <summary>
    /// The position of the role: Lower has more authority
    /// </summary>
    int Position { get; set; }

    /// <summary>
    /// The planet permissions for the role
    /// </summary>
    long Permissions { get; set; }

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
    byte Red { get; set; }
    byte Green { get; set; }
    byte Blue { get; set; }

    // Formatting options
    bool Bold { get; set; }

    bool Italics { get; set; }

    public int GetAuthority() =>
        ISharedPlanetRole.GetAuthority(this);

    public Color GetColor() =>
        ISharedPlanetRole.GetColor(this);


    public string GetColorHex() =>
        ISharedPlanetRole.GetColorHex(this);

    public bool HasPermission(PlanetPermission perm) =>
        ISharedPlanetRole.HasPermission(this, perm);

    public static int GetAuthority(ISharedPlanetRole role) =>
        int.MaxValue - role.Position - 1; // Subtract one so owner can have higher

    public static Color GetColor(ISharedPlanetRole role) =>
        Color.FromArgb(role.Red, role.Green, role.Blue);

    public static string GetColorHex(ISharedPlanetRole role)
    {
        Color c = role.GetColor();
        return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
    }

    public static bool HasPermission(ISharedPlanetRole role, PlanetPermission perm)
        => Permission.HasPermission(role.Permissions, perm);

}

