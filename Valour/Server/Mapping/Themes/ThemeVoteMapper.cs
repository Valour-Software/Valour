using Valour.Server.Models.Themes;

namespace Valour.Server.Mapping.Themes;

public static class ThemeVoteMapper
{
    public static ThemeVote ToModel(this Valour.Database.Themes.ThemeVote vote)
    {
        if (vote is null)
            return null;
        
        return new ThemeVote()
        {
            Id = vote.Id,
            ThemeId = vote.ThemeId,
            UserId = vote.UserId,
            Sentiment = vote.Sentiment,
            CreatedAt = vote.CreatedAt
        };
    }
    
    public static Valour.Database.Themes.ThemeVote ToDatabase(this ThemeVote vote)
    {
        if (vote is null)
            return null;
        
        return new Valour.Database.Themes.ThemeVote()
        {
            Id = vote.Id,
            ThemeId = vote.ThemeId,
            UserId = vote.UserId,
            Sentiment = vote.Sentiment,
            CreatedAt = vote.CreatedAt
        };
    }
}