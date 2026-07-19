using Valour.Shared.Models;

namespace Valour.Server.Models;

public class GifFavorite : ServerModel<long>, ISharedGifFavorite
{
    public long UserId { get; set; }
    public string Provider { get; set; } = "klipy";
    public string ProviderId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string PreviewUrl { get; set; } = string.Empty;
    public string GifUrl { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
}
