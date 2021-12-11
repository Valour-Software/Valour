using System.Text.Json.Serialization;

namespace Valour.Shared.Items.Users;

public class TokenRequest
{
    [JsonPropertyName("Email")]
    public string Email { get; set; }

    [JsonPropertyName("Password")]
    public string Password { get; set; }
}

