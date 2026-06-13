using System.Text.Json.Serialization;
using Valour.Sdk.Client;
using Valour.Sdk.ModelLogic;
using Valour.Shared.Models.Threads;

namespace Valour.Sdk.Models.Threads;

public class ThreadComment : ClientPlanetModel<ThreadComment, long>, ISharedThreadComment
{
    public override string BaseRoute => ISharedThreadComment.GetBaseRoute(PlanetId, ThreadId);

    public override string IdRoute => ISharedThreadComment.GetIdRoute(PlanetId, ThreadId, Id);

    public long PlanetId { get; set; }
    public long ThreadId { get; set; }
    public long? ParentCommentId { get; set; }
    public int Depth { get; set; }
    public long AuthorUserId { get; set; }
    public long? AuthorMemberId { get; set; }
    public string Content { get; set; }
    public DateTime TimeCreated { get; set; }
    public DateTime? EditedTime { get; set; }
    public int BoostCount { get; set; }
    public int ReplyCount { get; set; }
    public bool IsDeleted { get; set; }

    protected override long? GetPlanetId() => PlanetId;

    [JsonConstructor]
    private ThreadComment() : base()
    {
    }

    public ThreadComment(ValourClient client) : base(client)
    {
    }

    public override ThreadComment AddToCache(ModelInsertFlags flags = ModelInsertFlags.None)
    {
        return Planet.ThreadComments.Put(this, flags);
    }

    public override ThreadComment RemoveFromCache(bool skipEvents = false)
    {
        return Planet.ThreadComments.Remove(this, skipEvents);
    }
}
