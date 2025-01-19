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
}