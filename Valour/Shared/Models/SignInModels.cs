namespace Valour.Shared.Models;

/// <summary>
/// Services a person can sign in with besides a password. The values are the
/// route segments used by api/auth/external/{provider}.
/// </summary>
public static class ExternalAuthProviders
{
    public const string Google = "google";
    public const string Discord = "discord";
}

public enum ExternalAuthIntent
{
    /// <summary>Sign in, or start creating an account.</summary>
    Login = 0,

    /// <summary>Add the account to the signed-in user's sign-in methods.</summary>
    Link = 1,

    /// <summary>Confirm the signed-in user's identity before a sensitive change.</summary>
    Reauth = 2,
}

/// <summary>
/// Where the server sends the result after the provider's page. Each kind has
/// its own fixed return address, so the result cannot be sent elsewhere.
/// </summary>
public enum ExternalAuthClient
{
    /// <summary>A browser popup. The web app collects the result from the server.</summary>
    Web = 0,

    /// <summary>The Android, iOS, and Mac apps, through the gg.valour.app:// scheme.</summary>
    Android = 1,

    /// <summary>A desktop app listening on a loopback port.</summary>
    Desktop = 2,
}

public class ExternalAuthBeginRequest
{
    public ExternalAuthIntent Intent { get; set; }
    public ExternalAuthClient Client { get; set; }

    /// <summary>The loopback port a desktop app listens on.</summary>
    public int? LoopbackPort { get; set; }

    /// <summary>
    /// Base64url SHA-256 of a random verifier the client keeps. Redeeming the
    /// result requires the verifier, so a result intercepted on the way back
    /// is useless to anyone else.
    /// </summary>
    public string VerifierHash { get; set; }

    /// <summary>Required to link: a recent proof that the account owner is present.</summary>
    public string ReauthProof { get; set; }
}

public class ExternalAuthBeginResponse
{
    public string AuthorizationUrl { get; set; }

    /// <summary>
    /// Identifies the flow. A web client collects the result with it from
    /// api/auth/external/result, since the popup can't hand it back directly.
    /// </summary>
    public string FlowId { get; set; }
}

/// <summary>The outcome of a provider sign-in, with a ticket or a message.</summary>
public class ExternalAuthResultResponse
{
    /// <summary>One of <see cref="ExternalAuthResults"/>.</summary>
    public string Result { get; set; }
    public string Ticket { get; set; }
    public string Message { get; set; }
}

/// <summary>
/// The "result" parameter the server sends back to the client after the
/// provider's page, with a "ticket" or "message" alongside.
/// </summary>
public static class ExternalAuthResults
{
    /// <summary>The account is linked. Redeem the ticket at api/users/token.</summary>
    public const string Login = "login";

    /// <summary>No Valour account uses this sign-in. The ticket starts registration.</summary>
    public const string Register = "register";

    /// <summary>
    /// The account can be linked. Redeem the ticket at
    /// api/users/me/signin-methods/link from the session that started the flow.
    /// </summary>
    public const string Linked = "linked";

    /// <summary>
    /// Identity confirmed. Redeem the ticket at api/users/me/reauth from the
    /// session that started the flow to get a proof.
    /// </summary>
    public const string Reauth = "reauth";

    public const string Error = "error";
}

/// <summary>
/// A ticket from a Google or Discord sign-in, with the verifier the client
/// generated when it started that sign-in.
/// </summary>
public class ExternalTicketRequest
{
    public string Ticket { get; set; }
    public string Verifier { get; set; }
}

/// <summary>
/// What the sign-up form can prefill from the linked account.
/// </summary>
public class ExternalRegistrationInfo
{
    public string Provider { get; set; }
    public string Email { get; set; }
    public string SuggestedUsername { get; set; }
    public bool HasAvatar { get; set; }
}

/// <summary>
/// One way the account can sign in, as listed in Connections settings.
/// </summary>
public class SignInMethodInfo
{
    public long Id { get; set; }

    /// <summary>Password, Google, Discord, or DeviceKey.</summary>
    public string Type { get; set; }

    public string DisplayName { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }

    /// <summary>For device keys, the key ID the device keeps.</summary>
    public string DeviceKeyId { get; set; }
}

public class ReauthRequest
{
    public string Password { get; set; }

    public string DeviceKeyId { get; set; }
    public string DeviceChallengeId { get; set; }
    public string DeviceSignature { get; set; }

    /// <summary>The ticket from signing in again with a linked account, and its verifier.</summary>
    public string ExternalTicket { get; set; }
    public string ExternalVerifier { get; set; }
}

public class ReauthResponse
{
    public string Proof { get; set; }
    public DateTime ExpiresAt { get; set; }
}

public class RemoveSignInMethodRequest
{
    public string ReauthProof { get; set; }
}

public class SetPasswordRequest
{
    public string NewPassword { get; set; }
    public string ReauthProof { get; set; }
}

public class DeviceKeyRegisterRequest
{
    /// <summary>Base64 DER SubjectPublicKeyInfo of a P-256 key.</summary>
    public string PublicKey { get; set; }

    public string DeviceName { get; set; }
    public string ReauthProof { get; set; }

    /// <summary>
    /// Required when the account has two-factor authentication, because
    /// fingerprint sign-in skips the authenticator code afterwards.
    /// </summary>
    public string MultiFactorCode { get; set; }
}

public class DeviceKeyRegisterResponse
{
    public string KeyId { get; set; }
}

public class DeviceChallengeRequest
{
    public string KeyId { get; set; }
}

public class DeviceChallengeResponse
{
    public string ChallengeId { get; set; }

    /// <summary>Base64 bytes to sign with SHA-256 ECDSA.</summary>
    public string Challenge { get; set; }
}
