namespace Valour.Shared.Models.Themes;

/// <summary>
/// Theme meta allows for viewing theme information without the need for the full theme object.
/// </summary>
public interface ISharedThemeMeta : ISharedModel<long>
{
    public long AuthorId { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public bool HasCustomBanner { get; set; }
    public bool HasAnimatedBanner { get; set; }
    
    public string MainColor1 { get; set; }
    public string PastelCyan { get; set; }

    public string AuthorName { get; set; }
    public int Upvotes { get; set; }
    public int Downvotes { get; set; }

    /// <summary>
    /// The current user's vote sentiment on this theme. Null if no vote.
    /// True = upvote, False = downvote.
    /// </summary>
    public bool? MySentiment { get; set; }

    /// <summary>
    /// The current user's vote id on this theme. Null if no vote.
    /// </summary>
    public long? MyVoteId { get; set; }
}