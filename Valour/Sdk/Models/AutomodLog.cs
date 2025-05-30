using Valour.Sdk.Client;
using Valour.Sdk.ModelLogic;
using Valour.Shared.Models.Staff;

namespace Valour.Sdk.Models;

public class AutomodLog : ClientPlanetModel<AutomodLog, Guid>, ISharedAutomodLog
{
    public override string BaseRoute => $"api/planets/{PlanetId}/automod/logs";
    public override string IdRoute => $"{BaseRoute}/{Id}";

    public Guid TriggerId { get; set; }
    public long MemberId { get; set; }
    public long? MessageId { get; set; }
    public DateTime TimeTriggered { get; set; }
    public long PlanetId { get; set; }

    [JsonConstructor]
    private AutomodLog() : base() { }
    public AutomodLog(ValourClient client) : base(client) { }

    protected override long? GetPlanetId() => PlanetId;

    public override AutomodLog AddToCache(ModelInsertFlags flags = ModelInsertFlags.None)
    {
        return this;
    }

    public override AutomodLog RemoveFromCache(bool skipEvents = false) => this;
}

