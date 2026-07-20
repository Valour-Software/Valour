namespace Valour.Shared.Models.Staff;

public class DisableUserRequest
{
    public long UserId { get; set; }
    public bool Value { get; set; }
    public string Reason { get; set; }
}
