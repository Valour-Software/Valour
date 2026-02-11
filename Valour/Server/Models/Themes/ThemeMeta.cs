using Valour.Shared.Models.Themes;

namespace Valour.Server.Models.Themes;

public class ThemeMeta : ISharedThemeMeta
{
    public long Id { get; set; }
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
}