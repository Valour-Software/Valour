﻿using Valour.Shared.Authorization;

namespace Valour.Shared.Models;

public interface ISharedAuthToken
{
    string Id { get; set; }

    /// <summary>
    /// The ID of the app that has been issued this token
    /// </summary>
    string AppId { get; set; }

    /// <summary>
    /// The user that this token is valid for
    /// </summary>
    long UserId { get; set; }

    /// <summary>
    /// The scope of the permissions this token is valid for
    /// </summary>
    long Scope { get; set; }

    /// <summary>
    /// The time that this token was issued
    /// </summary>
    DateTime TimeCreated { get; set; }

    /// <summary>
    /// The time that this token will expire
    /// </summary>
    DateTime TimeExpires { get; set; }

    /// <summary>
    /// Helper method for scope checking
    /// </summary>
    public static bool HasScope(Permission permission, ISharedAuthToken token) =>
        Permission.HasPermission(token.Scope, permission);
}

