using Valour.Sdk.Client;
using Valour.Sdk.ModelLogic;
using Valour.Shared.Models.Staff;

namespace Valour.Sdk.Models;

public class AutomodAction : ClientPlanetModel<AutomodAction, Guid>, ISharedAutomodAction
{
    public override string BaseRoute => $"api/planets/{PlanetId}/automod/triggers/{TriggerId}/actions";
    public override string IdRoute => $"{BaseRoute}/{Id}";

    public int Strikes { get; set; }
    public bool UseGlobalStrikes { get; set; }
    public Guid TriggerId { get; set; }
    public long MemberAddedBy { get; set; }
    public AutomodActionType ActionType { get; set; }
    public long PlanetId { get; set; }
    public long TargetMemberId { get; set; }
    public long? MessageId { get; set; }
    public long? RoleId { get; set; }
    public DateTime? Expires { get; set; }
    public string Message { get; set; }

    [JsonConstructor]
    private AutomodAction() : base() { }
    public AutomodAction(ValourClient client) : base(client) { }

    protected override long? GetPlanetId() => PlanetId;

    public override AutomodAction AddToCache(ModelInsertFlags flags = ModelInsertFlags.None)
    {
        return this;
    }

    public override AutomodAction RemoveFromCache(bool skipEvents = false) => this;
}
