#nullable enable annotations

internal class ServerAuthResult {
    public bool Success { get; set; }
    public string? Message { get; set; }
    public AuthToken? Token { get; set; }
    public bool RequiresMultiAuth { get; set; } = false;
    public bool RequiresEmailVerification { get; set; } = false;
    public bool Disabled { get; set; } = false;

    /// <summary>
    /// A proof of identity for the new session, so a change right after
    /// signing in, like turning on fingerprint sign-in, doesn't ask again.
    /// </summary>
    public string? ReauthProof { get; set; }
}
