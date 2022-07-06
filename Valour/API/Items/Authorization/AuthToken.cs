using Valour.Shared.Authorization;
using Valour.Shared.Items.Authorization;

namespace Valour.Api.Items.Authorization;

public class AuthToken : ISharedAuthToken
{
    public string Id { get; set; }

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
    /// Returns whether the auth token has the given scope
    /// </summary>
    public bool HasScope(Permission permission) =>
        ISharedAuthToken.HasScope(permission, this);
}

