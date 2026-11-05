namespace Valour.Server.Models;

public class EmailConfirmCode
{
    public string Code { get; set; }
    public long UserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string NewEmail { get; set; }
    public string SessionId { get; set; }
}
