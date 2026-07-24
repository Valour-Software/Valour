using System.Security.Cryptography;
using System.Text;

namespace Valour.Shared.Models;

/// <summary>
/// A session-management view of an auth token that deliberately omits the
/// token secret. The raw token id IS the bearer credential, so it must never
/// be sent back to a client - listing sessions would otherwise hand out every
/// session key the account owns. Sessions are addressed by <see cref="Handle"/>
/// instead, which is a one-way hash of the secret.
/// </summary>
public class AuthTokenInfo
{
    /// <summary>
    /// Non-secret, stable identifier for this session, used to revoke it.
    /// Derived from the token secret so no schema change is needed, but it
    /// cannot be reversed back into a usable credential.
    /// </summary>
    public string Handle { get; set; }

    /// <summary>
    /// The ID of the app that has been issued this token
    /// </summary>
    public string AppId { get; set; }

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
    /// Whether this is the session making the request. Computed on the server
    /// because the client can no longer compare against the raw token id.
    /// </summary>
    public bool IsCurrent { get; set; }

    /// <summary>
    /// Derives the public handle for a token secret.
    /// </summary>
    public static string GetHandle(string tokenId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tokenId))).ToLowerInvariant();
}
