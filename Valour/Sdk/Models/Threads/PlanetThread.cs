using System.Text.Json.Serialization;
using Valour.Sdk.Client;
using Valour.Sdk.ModelLogic;
using Valour.Shared.Models;
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
    public bool Nsfw { get; set; }
    public int BoostCount { get; set; }
    public int CommentCount { get; set; }

    /// <summary>
    /// Server-managed provenance for content imported from another service.
    /// </summary>
    public string ImportSource { get; set; }

    public List<MessageAttachment> Attachments { get; set; }

    /// <summary>
    /// Feed-only author snapshot. Hydrated query responses populate this so a
    /// page of cards does not issue one user request per author.
    /// </summary>
    [IgnoreRealtimeChanges]
    public User AuthorUser { get; set; }

    /// <summary>
    /// Feed-only planet-member snapshot for nickname and member avatar display.
    /// </summary>
    [IgnoreRealtimeChanges]
    public PlanetMember AuthorMember { get; set; }

    /// <summary>
    /// Feed-only role shown beside the author. This deliberately carries only
    /// the displayed role instead of every role on every represented planet.
    /// </summary>
    [IgnoreRealtimeChanges]
    public PlanetRole AuthorRole { get; set; }

    /// <summary>
    /// Feed-only presence snapshot, shared by all cards from the same planet.
    /// </summary>
    [IgnoreRealtimeChanges]
    public PlanetPresenceSummary Presence { get; set; }

    /// <summary>
    /// Feed-only viewer state. Null means an older server did not hydrate it.
    /// </summary>
    [IgnoreRealtimeChanges]
    public bool? ViewerHasBoosted { get; set; }

    /// <summary>
    /// True if this thread is the one currently pinned on its planet. Pinning lives
    /// on the planet (one pin per planet), so this is derived from the cached planet.
    /// </summary>
    [JsonIgnore]
    public bool IsPinned => GetPlanet(false)?.PinnedThreadId == Id;

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
        // Feed hydration is intentionally excluded from realtime model diffs,
        // but query engines can race (global and planet feeds often load the
        // same thread together). Merge snapshots before ModelStore returns the
        // existing master copy so the richer response always wins.
        if (Planet.Threads.TryGet(Id, out var existing))
        {
            existing.AuthorUser = AuthorUser ?? existing.AuthorUser;
            existing.AuthorMember = AuthorMember ?? existing.AuthorMember;
            existing.AuthorRole = AuthorRole ?? existing.AuthorRole;
            existing.Presence = Presence ?? existing.Presence;
            existing.ViewerHasBoosted = ViewerHasBoosted ?? existing.ViewerHasBoosted;
        }

        return Planet.Threads.Put(this, flags);
    }

    public override PlanetThread RemoveFromCache(bool skipEvents = false)
    {
        return Planet.Threads.Remove(this, skipEvents);
    }

    public override void SyncSubModels(ModelInsertFlags flags = ModelInsertFlags.None)
    {
        AuthorMember = AuthorMember?.Sync(Client, flags);
        AuthorUser = AuthorMember?.User ?? AuthorUser?.Sync(Client, flags);
    }
}
