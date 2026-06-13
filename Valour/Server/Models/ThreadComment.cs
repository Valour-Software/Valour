using Valour.Shared.Models.Threads;

namespace Valour.Server.Models;

public class ThreadComment : ServerModel<long>, ISharedThreadComment
{
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
}
