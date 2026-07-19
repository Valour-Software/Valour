using Valour.Sdk.Client;
using Valour.Sdk.ModelLogic;
using Valour.Shared.Models;

namespace Valour.Sdk.Models;

public class GifFavorite : ClientModel<GifFavorite, long>, ISharedGifFavorite
{
    public override string BaseRoute => ISharedGifFavorite.BaseRoute;

    public long UserId { get; set; }
    public string Provider { get; set; } = "klipy";
    public string ProviderId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string PreviewUrl { get; set; } = string.Empty;
    public string GifUrl { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }

    [JsonConstructor]
    private GifFavorite() : base() { }
    public GifFavorite(ValourClient client) : base(client) { }

    public override GifFavorite AddToCache(ModelInsertFlags flags = ModelInsertFlags.None) => this;
    public override GifFavorite RemoveFromCache(bool skipEvents) => this;
}
