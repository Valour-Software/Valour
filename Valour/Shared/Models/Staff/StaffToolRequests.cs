namespace Valour.Shared.Models.Staff;

/// <summary>
/// Staff lookup of a user by username, username#tag, email, or user id.
/// PII access: always audit-logged with the given reason.
/// </summary>
public class StaffUserLookupRequest
{
    public string Identifier { get; set; }
    public string Reason { get; set; }
}

public class StaffUserLookupResult
{
    public long UserId { get; set; }
    public string Name { get; set; }
    public string Tag { get; set; }
    public string Email { get; set; }
    public bool EmailVerified { get; set; }
    public bool Disabled { get; set; }
    public bool Bot { get; set; }
    public long? OwnerId { get; set; }
    public string PriorName { get; set; }
    public DateTime? NameChangeTime { get; set; }
    public bool HidePriorName { get; set; }
    public DateTime TimeCreated { get; set; }
    public bool HasMfa { get; set; }
    public DateTime? PendingMfaRemovalAt { get; set; }
}

public class StaffResetUsernameRequest
{
    public long UserId { get; set; }
    public string Reason { get; set; }
}

public class StaffSetPriorNameHiddenRequest
{
    public long UserId { get; set; }
    public bool Hidden { get; set; }
    public string Reason { get; set; }
}

/// <summary>
/// Staff never see or set passwords. This triggers the normal password
/// reset email to the account's address, optionally ending all sessions.
/// </summary>
public class StaffPasswordResetRequest
{
    public long UserId { get; set; }
    public bool InvalidateSessions { get; set; }
    public string Reason { get; set; }
}

/// <summary>
/// Schedules MFA removal after a safety delay. The account is emailed
/// immediately and the removal can be cancelled before it executes.
/// </summary>
public class StaffMfaRemovalRequest
{
    public long UserId { get; set; }
    public string Reason { get; set; }
}
