using Valour.Shared.Models.Themes;

namespace Valour.Sdk.Models.Themes;

public class ThemeVote : ISharedThemeVote
{
    public long Id { get; set; }
    public long ThemeId { get; set; }
    public long UserId { get; set; } 
    public bool Sentiment { get; set; }
    public DateTime CreatedAt { get; set; }
}