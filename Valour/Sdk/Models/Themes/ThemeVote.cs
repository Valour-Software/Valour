using Valour.Sdk.ModelLogic;
using Valour.Shared.Models.Themes;

namespace Valour.Sdk.Models.Themes;

public class ThemeVote : ClientModel<ThemeVote, long>, ISharedThemeVote
{
    public override string BaseRoute => $"api/themes/{ThemeId}/votes";
    
    public long ThemeId { get; set; }
    public long UserId { get; set; } 
    public bool Sentiment { get; set; }
    public DateTime CreatedAt { get; set; }
    
    public override ThemeVote AddToCache()
    {
        // we dont cache theme votes
        return this;
    }

    public override ThemeVote TakeAndRemoveFromCache()
    {
        // we dont cache theme votes
        return this;
    }
}