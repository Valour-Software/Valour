namespace Valour.Shared.Models.Staff;

public enum ModerationActionSource
{
    Manual = 0,
    Automod = 1
}

public enum ModerationActionType
{
    Kick = 0,
    Ban = 1,
    BanUpdated = 2,
    Unban = 3,
    AddRole = 4,
    RemoveRole = 5,
    DeleteMessage = 6,
    BlockMessage = 7,
    Respond = 8,
    ResolveReport = 9,
    DismissReport = 10,
    DeleteThread = 11,
    LockThread = 12,
    UnlockThread = 13,
    PinThread = 14,
    UnpinThread = 15,
    DeleteThreadComment = 16
}

public interface ISharedModerationAuditLog : ISharedPlanetModel<long>
{
    long? ActorUserId { get; set; }
    long? TargetUserId { get; set; }
    long? TargetMemberId { get; set; }
    long? MessageId { get; set; }
    Guid? TriggerId { get; set; }
    ModerationActionSource Source { get; set; }
    ModerationActionType ActionType { get; set; }
    string? Details { get; set; }
    DateTime TimeCreated { get; set; }
}
