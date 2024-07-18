namespace Valour.Shared.Models;

public class UserQueryRequest
{
    public string UsernameAndTag { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
}