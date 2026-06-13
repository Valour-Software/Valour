namespace Valour.Shared.Models.Threads;

public interface ISharedThreadBoost : ISharedModel<long>
{
    long ThreadId { get; set; }
    long PlanetId { get; set; }
    long UserId { get; set; }
    DateTime CreatedAt { get; set; }
}

public interface ISharedThreadCommentBoost : ISharedModel<long>
{
    long CommentId { get; set; }
    long ThreadId { get; set; }
    long PlanetId { get; set; }
    long UserId { get; set; }
    DateTime CreatedAt { get; set; }
}

/// <summary>
/// Request body for checking which of a set of threads or comments the current user has boosted
/// </summary>
public class BoostLookupRequest
{
    public List<long> Ids { get; set; }
}
