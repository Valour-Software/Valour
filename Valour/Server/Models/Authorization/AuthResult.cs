#nullable enable annotations

internal class ServerAuthResult {
    public bool Success { get; set; }
    public string? Message { get; set; }
    public AuthToken? Token { get; set; }
    public bool RequiresMultiAuth { get; set; } = false;
    public bool RequiresEmailVerification { get; set; } = false;
    public bool Disabled { get; set; } = false;
}
