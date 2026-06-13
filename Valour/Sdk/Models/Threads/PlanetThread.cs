using System.Text.Json.Serialization;
using Valour.Sdk.Client;
using Valour.Sdk.ModelLogic;
using Valour.Shared.Models.Threads;

namespace Valour.Sdk.Models.Threads;

public class PlanetThread : ClientPlanetModel<PlanetThread, long>, ISharedPlanetThread
{
    public override string BaseRoute => ISharedPlanetThread.GetBaseRoute(PlanetId);

    public override string IdRoute => ISharedPlanetThread.GetIdRoute(PlanetId, Id);

    public long PlanetId { get; set; }
    public long AuthorUserId { get; set; }
    public long? AuthorMemberId { get; set; }
    public string Title { get; set; }
    public string Content { get; set; }
    public DateTime TimeCreated { get; set; }
    public DateTime? EditedTime { get; set; }
    public bool IsLocked { get; set; }
    public bool IsPinned { get; set; }
    public bool Nsfw { get; set; }
    public int BoostCount { get; set; }
    public int CommentCount { get; set; }

    public List<MessageAttachment> Attachments { get; set; }

    protected override long? GetPlanetId() => PlanetId;

    [JsonConstructor]
    private PlanetThread() : base()
    {
    }

    public PlanetThread(ValourClient client) : base(client)
    {
    }

    public override PlanetThread AddToCache(ModelInsertFlags flags = ModelInsertFlags.None)
    {
        return Planet.Threads.Put(this, flags);
    }

    public override PlanetThread RemoveFromCache(bool skipEvents = false)
    {
        return Planet.Threads.Remove(this, skipEvents);
    }
}
