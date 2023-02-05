namespace Valour.Server.Models;

public class UserEmail
{
    public string Email { get; set; }
    public bool Verified { get; set; }
    public long UserId { get; set; }
}
