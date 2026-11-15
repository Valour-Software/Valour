using Valour.Sdk.Client;
using Valour.Sdk.ModelLogic;
using Valour.Shared.Models.Staff;

namespace Valour.Sdk.Models;

public class ModerationAuditLog : ClientPlanetModel<ModerationAuditLog, long>, ISharedModerationAuditLog
{
    public override string BaseRoute => $"api/planets/{PlanetId}/moderation/audit";
    public override string IdRoute => $"{BaseRoute}/{Id}";

    public long PlanetId { get; set; }
    public long? ActorUserId { get; set; }
    public long? TargetUserId { get; set; }
    public long? TargetMemberId { get; set; }
    public long? MessageId { get; set; }
    public Guid? TriggerId { get; set; }
    public ModerationActionSource Source { get; set; }
    public ModerationActionType ActionType { get; set; }
    public string? Details { get; set; }
    public DateTime TimeCreated { get; set; }

    [JsonConstructor]
    private ModerationAuditLog() : base() { }
    public ModerationAuditLog(ValourClient client) : base(client) { }

    protected override long? GetPlanetId() => PlanetId;
}
