using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Server.Models;

public class AuthToken : ServerModel<string>, ISharedAuthToken
{
    /// <summary>
    /// The ID of the app that has been issued this token
    /// </summary>
    public string AppId { get; set; }

    /// <summary>
    /// The user that this token is valid for
    /// </summary>
    public long UserId { get; set; }

    /// <summary>
    /// The scope of the permissions this token is valid for
    /// </summary>
    public long Scope { get; set; }

    /// <summary>
    /// The time that this token was issued
    /// </summary>
    public DateTime TimeCreated { get; set; }

    /// <summary>
    /// The time that this token will expire
    /// </summary>
    public DateTime TimeExpires { get; set; }

    /// <summary>
    /// The IP address this token was issued to originally
    /// </summary>
    public string IssuedAddress { get; set; }
    
    /// <summary>
    /// Returns whether the auth token has the given scope
    /// </summary>
    public bool HasScope(Permission permission) =>
        ISharedAuthToken.HasScope(permission, this);
}

