namespace Valour.Shared.Models.Staff;

public enum StaffActionType
{
    DisableAccount = 0,
    EnableAccount = 1,
    DeleteAccount = 2,
    VerifyEmail = 3,
    LookupUser = 4,
    ViewOwnedBots = 5,
    ResetUsername = 6,
    HidePriorName = 7,
    ShowPriorName = 8,
    TriggerPasswordReset = 9,
    ScheduleMfaRemoval = 10,
    CancelMfaRemoval = 11,
    ExecuteMfaRemoval = 12
}

public interface ISharedStaffAuditLog
{
    long Id { get; set; }
    long StaffUserId { get; set; }
    StaffActionType ActionType { get; set; }
    long? TargetUserId { get; set; }
    string Reason { get; set; }
    string? Details { get; set; }
    DateTime TimeCreated { get; set; }
}

/// <summary>
/// Client-facing audit log entry, enriched with display names so the staff
/// UI does not need to resolve every actor and target separately.
/// </summary>
public class StaffAuditLogEntry : ISharedStaffAuditLog
{
    public long Id { get; set; }
    public long StaffUserId { get; set; }
    public string StaffName { get; set; }
    public StaffActionType ActionType { get; set; }
    public long? TargetUserId { get; set; }
    public string TargetName { get; set; }
    public string Reason { get; set; }
    public string? Details { get; set; }
    public DateTime TimeCreated { get; set; }
}
