using Valour.Sdk.ModelLogic;
using Valour.Shared.Models;
using Valour.Shared.Models.Themes;

namespace Valour.Sdk.Models.Themes;

public class ThemeMeta : ClientModel<ThemeMeta, long>, ISharedThemeMeta
{
    public long AuthorId { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public bool HasCustomBanner { get; set; }
    public bool HasAnimatedBanner { get; set; }
    
    public string MainColor1 { get; set; }
    public string PastelCyan { get; set; }
    
    public string GetBannerUrl(ThemeBannerFormat format = ThemeBannerFormat.Webp)
    {
        return ISharedTheme.GetBannerUrl(this, format);
    }

    public override ThemeMeta AddToCache(ModelInsertFlags flags = ModelInsertFlags.None)
    {
        return this;
    }
    
    public override ThemeMeta RemoveFromCache(bool skipEvents = false)
    {
        return this;
    }
}