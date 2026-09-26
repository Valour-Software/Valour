#nullable enable

public class AuthResult {
    public bool Success { get; set; }
    public string? Message { get; set; }
    public AuthToken? Token { get; set; }
    public bool RequiresMultiAuth { get; set; } = false;
    public bool RequiresEmailVerification { get; set; } = false;
    public bool Disabled { get; set; } = false;
    public int Code { get; set; }

    /// <summary>
    /// A proof of identity for the new session, accepted by sensitive changes
    /// for a few minutes after signing in.
    /// </summary>
    public string? ReauthProof { get; set; }
}