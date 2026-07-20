namespace Valour.Shared.Models.Staff;

public class DeleteUserRequest
{
    public long UserId { get; set; }
    public string Reason { get; set; }
}
