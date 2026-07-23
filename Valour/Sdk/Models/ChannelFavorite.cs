using Valour.Sdk.Client;
using Valour.Sdk.ModelLogic;
using Valour.Shared.Models;

namespace Valour.Sdk.Models;

public class ChannelFavorite : ClientModel<ChannelFavorite, long>, ISharedChannelFavorite
{
    public override string BaseRoute => ISharedChannelFavorite.BaseRoute;

    public long UserId { get; set; }
    public long ChannelId { get; set; }
    public long PlanetId { get; set; }
    public int Position { get; set; }

    [JsonConstructor]
    private ChannelFavorite() : base() { }
    public ChannelFavorite(ValourClient client) : base(client) { }

    public override ChannelFavorite AddToCache(ModelInsertFlags flags = ModelInsertFlags.None) => this;
    public override ChannelFavorite RemoveFromCache(bool skipEvents) => this;
}
