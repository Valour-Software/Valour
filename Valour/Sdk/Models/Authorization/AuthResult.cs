#nullable enable

public class AuthResult {
    public bool Success { get; set; }
    public string? Message { get; set; }
    public AuthToken? Token { get; set; }
    public bool RequiresMultiAuth { get; set; } = false;
    public int Code { get; set; }
}