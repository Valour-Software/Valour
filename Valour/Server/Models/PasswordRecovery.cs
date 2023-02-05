namespace Valour.Server.Models;

public class PasswordRecovery
{
    public string Code { get; set; }
    public long UserId { get; set; }
}