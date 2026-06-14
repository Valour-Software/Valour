using Valour.Shared.Models.Threads;

namespace Valour.Server.Models;

public class PlanetThread : ServerModel<long>, ISharedPlanetThread
{
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

    public List<Valour.Sdk.Models.MessageAttachment> Attachments { get; set; }
}
