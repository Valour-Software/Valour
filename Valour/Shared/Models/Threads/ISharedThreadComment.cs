namespace Valour.Shared.Models.Threads;

public interface ISharedThreadComment : ISharedPlanetModel<long>
{
    public static string GetBaseRoute(long planetId, long threadId) =>
        $"api/planets/{planetId}/threads/{threadId}/comments";
    public static string GetIdRoute(long planetId, long threadId, long id) =>
        $"{GetBaseRoute(planetId, threadId)}/{id}";
    public static string GetQueryRoute(long planetId, long threadId) =>
        $"{GetBaseRoute(planetId, threadId)}/query";
    public static string GetBoostRoute(long planetId, long threadId, long id) =>
        $"{GetIdRoute(planetId, threadId, id)}/boost";
    public static string GetBoostLookupRoute(long planetId, long threadId) =>
        $"{GetBaseRoute(planetId, threadId)}/boosts/lookup";

    public const int MaxContentLength = 2048;

    /// <summary>
    /// The maximum nesting depth for comments. Replies at the cap attach to their parent's level.
    /// </summary>
    public const int MaxDepth = 6;

    /// <summary>
    /// The thread this comment belongs to
    /// </summary>
    long ThreadId { get; set; }

    /// <summary>
    /// The comment this is a reply to, or null for top-level comments
    /// </summary>
    long? ParentCommentId { get; set; }

    /// <summary>
    /// Nesting depth (0 for top-level comments)
    /// </summary>
    int Depth { get; set; }

    long AuthorUserId { get; set; }
    long? AuthorMemberId { get; set; }

    /// <summary>
    /// Markdown-capable comment content
    /// </summary>
    string Content { get; set; }

    DateTime TimeCreated { get; set; }
    DateTime? EditedTime { get; set; }

    /// <summary>
    /// Denormalized count of boosts on this comment
    /// </summary>
    int BoostCount { get; set; }

    /// <summary>
    /// Denormalized count of direct replies to this comment
    /// </summary>
    int ReplyCount { get; set; }

    /// <summary>
    /// Deleted comments remain as tombstones to preserve the reply tree
    /// </summary>
    bool IsDeleted { get; set; }

    /// <summary>
    /// Non-null when this comment originated in an external import.
    /// </summary>
    string ImportSource { get; set; }
}
