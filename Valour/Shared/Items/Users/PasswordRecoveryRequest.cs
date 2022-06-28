using System.Text.Json.Serialization;

namespace Valour.Shared.Items.Users;

/// <summary>
/// Used to request a password recovery operation
/// </summary>
public class PasswordRecoveryRequest
{
    public string Password { get; set; }
    public string Code { get; set; }
}

