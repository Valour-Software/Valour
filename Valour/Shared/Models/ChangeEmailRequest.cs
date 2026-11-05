namespace Valour.Shared.Models;

public class ChangeEmailRequest
{
    public string NewEmail { get; set; }
    public string Password { get; set; }
    public string ReauthProof { get; set; }
    public string MultiFactorCode { get; set; }
}
