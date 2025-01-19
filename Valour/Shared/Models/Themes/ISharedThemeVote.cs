namespace Valour.Shared.Models.Themes;

public interface ISharedThemeVote : ISharedModel<long>
{
    public long ThemeId { get; set; }
    public long UserId { get; set; }
    
    /// <summary>
    /// True = Upvote, False = Downvote
    /// </summary>
    public bool Sentiment { get; set; }
    
    public DateTime CreatedAt { get; set; }
}