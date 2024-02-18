namespace Valour.Shared.Models.Themes;

public interface ISharedThemeVote
{
    public long Id { get; set; }
    public long ThemeId { get; set; }
    public long UserId { get; set; }
}