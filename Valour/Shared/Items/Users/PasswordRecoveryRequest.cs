using System.Text.Json.Serialization;

namespace Valour.Shared.Items.Users;

/// <summary>
/// Used to request a password recovery operation
/// </summary>
public class PasswordRecoveryRequest
{
    [JsonPropertyName("Password")]
    public string Password { get; set; }

    [JsonPropertyName("Code")]
    public string Code { get; set; }
}

