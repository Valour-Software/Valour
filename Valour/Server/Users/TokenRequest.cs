using System.Text.Json.Serialization;
using Valour.Shared.Items.Users;

namespace Valour.Server.Users;

public class TokenRequest : ISharedTokenRequest
{
    [JsonPropertyName("Email")]
    public string Email { get; set; }

    [JsonPropertyName("Password")]
    public string Password { get; set; }
}
