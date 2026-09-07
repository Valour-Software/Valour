namespace Valour.Shared.Models;

public enum DirectCallKind
{
    Audio = 0,
    Video = 1,
}

public enum DirectCallState
{
    Ringing = 0,
    Active = 1,
    Ended = 2,
}

public enum DirectCallMemberState
{
    Invited = 0,
    Joined = 1,
    Declined = 2,
    Left = 3,
}

public enum DirectCallEndReason
{
    None = 0,
    Completed = 1,
    Cancelled = 2,
    Declined = 3,
    Missed = 4,
    Failed = 5,
}

public class DirectCallMember
{
    public long UserId { get; set; }
    public bool IsCaller { get; set; }
    public DirectCallMemberState State { get; set; }
    public DateTime? RespondedAt { get; set; }
}

public class DirectCall
{
    public long Id { get; set; }
    public long ChannelId { get; set; }
    public long CallerUserId { get; set; }
    public DirectCallKind Kind { get; set; }
    public DirectCallState State { get; set; }
    public DirectCallEndReason EndReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public List<DirectCallMember> Members { get; set; } = [];
}

public class StartDirectCallRequest
{
    public long ChannelId { get; set; }
    public DirectCallKind Kind { get; set; }
}

public class AddDirectCallParticipantsRequest
{
    public List<long> UserIds { get; set; } = [];
}
