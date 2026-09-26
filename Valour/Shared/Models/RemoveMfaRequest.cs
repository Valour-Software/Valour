namespace Valour.Shared.Models;

public class RemoveMfaRequest
{
    public string Password { get; set; }

    /// <summary>
    /// A recent proof of identity from api/users/me/reauth or from signing in
    /// again with a linked account. Accepted instead of the password.
    /// </summary>
    public string ReauthProof { get; set; }

    /// <summary>
    /// A current code from the authenticator being removed. Required when the
    /// authenticator has finished setup.
    /// </summary>
    public string MultiFactorCode { get; set; }
}