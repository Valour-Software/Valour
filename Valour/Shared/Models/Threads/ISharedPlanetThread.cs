namespace Valour.Shared.Models.Threads;

/// <summary>
/// The sort modes supported by thread feeds
/// </summary>
public enum ThreadSortType
{
    Hot,
    New,
    Top
}

/// <summary>
/// The time period used by the 'Top' thread sort
/// </summary>
public enum ThreadTopPeriod
{
    Day,
    Week,
    All
}

public interface ISharedPlanetThread : ISharedPlanetModel<long>
{
    public static string GetBaseRoute(long planetId) => $"api/planets/{planetId}/threads";
    public static string GetIdRoute(long planetId, long id) => $"{GetBaseRoute(planetId)}/{id}";
    public static string GetQueryRoute(long planetId) => $"{GetBaseRoute(planetId)}/query";
    public static string GetBoostRoute(long planetId, long id) => $"{GetIdRoute(planetId, id)}/boost";
    public static string GetBoostLookupRoute(long planetId) => $"{GetBaseRoute(planetId)}/boosts/lookup";
    public static string GetLockRoute(long planetId, long id) => $"{GetIdRoute(planetId, id)}/lock";
    public static string GetPinRoute(long planetId, long id) => $"{GetIdRoute(planetId, id)}/pin";
    public static string GetDismissPinRoute(long planetId, long id) => $"{GetIdRoute(planetId, id)}/dismiss-pin";

    public const string FeedRoute = "api/threads/feed";
    public const string FeedBoostLookupRoute = "api/threads/boosts/lookup";

    public const int MaxTitleLength = 128;
    public const int MaxContentLength = 10000;
    public const int MaxAttachments = 8;

    /// <summary>
    /// The user that authored this thread
    /// </summary>
    long AuthorUserId { get; set; }

    /// <summary>
    /// The planet member that authored this thread
    /// </summary>
    long? AuthorMemberId { get; set; }

    /// <summary>
    /// Plain-text title of the thread
    /// </summary>
    string Title { get; set; }

    /// <summary>
    /// Markdown-capable body content
    /// </summary>
    string Content { get; set; }

    DateTime TimeCreated { get; set; }
    DateTime? EditedTime { get; set; }

    /// <summary>
    /// Locked threads do not accept new comments
    /// </summary>
    bool IsLocked { get; set; }

    bool Nsfw { get; set; }

    /// <summary>
    /// Denormalized count of boosts on this thread
    /// </summary>
    int BoostCount { get; set; }

    /// <summary>
    /// Denormalized count of comments on this thread
    /// </summary>
    int CommentCount { get; set; }

    /// <summary>
    /// Non-null when this thread originated in an external import.
    /// </summary>
    string ImportSource { get; set; }
}
