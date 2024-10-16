﻿using Valour.Shared.Authorization;


namespace Valour.Shared.Models;

public interface ISharedPlanetRole : ISharedPlanetModel, ISortableModel
{
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
    int Position { get; set; }

    public int GetAuthority() =>
        ISharedPlanetRole.GetAuthority(this);
    
    public bool HasPermission(PlanetPermission perm) =>
        ISharedPlanetRole.HasPermission(this, perm);

    public static int GetAuthority(ISharedPlanetRole role) =>
        int.MaxValue - role.Position - 1; // Subtract one so owner can have higher
    
    public static bool HasPermission(ISharedPlanetRole role, PlanetPermission perm)
        => Permission.HasPermission(role.Permissions, perm);

    int ISortableModel.GetSortPosition()
    {
        return Position;
    }
}

