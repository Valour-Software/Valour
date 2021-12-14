using System.Text.Json.Serialization;
using Valour.Shared.Items.Users;

namespace Valour.Api.Items.Users;

public class PasswordRecoveryRequest : ISharedPasswordRecoveryRequest
{
    [JsonPropertyName("Password")]
    public string Password { get; set; }

    [JsonPropertyName("Code")]
    public string Code { get; set; }
}