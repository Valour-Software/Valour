using Valour.Sdk.Client;
using Valour.Sdk.ModelLogic;
using Valour.Shared.Models.Staff;
using Valour.Shared.Models;

namespace Valour.Sdk.Models;

public class AutomodTrigger : ClientPlanetModel<AutomodTrigger, Guid>, ISharedAutomodTrigger, ISharedPlanetModel
{
    public override string BaseRoute => $"api/planets/{PlanetId}/automod/triggers";
    public override string IdRoute => $"{BaseRoute}/{Id}";

    public long PlanetId { get; set; }
    public long MemberAddedBy { get; set; }
    public AutomodTriggerType Type { get; set; }
    public string Name { get; set; }
    public string? TriggerWords { get; set; }

    [JsonConstructor]
    private AutomodTrigger() : base() { }
    public AutomodTrigger(ValourClient client) : base(client) { }

    protected override long? GetPlanetId() => PlanetId;

    public override AutomodTrigger AddToCache(ModelInsertFlags flags = ModelInsertFlags.None)
    {
        return this;
    }

    public override AutomodTrigger RemoveFromCache(bool skipEvents = false) => this;
}
