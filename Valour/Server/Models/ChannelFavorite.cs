using Valour.Shared.Models;

namespace Valour.Server.Models;

public class ChannelFavorite : ServerModel<long>, ISharedChannelFavorite
{
    public long UserId { get; set; }
    public long ChannelId { get; set; }
    public long PlanetId { get; set; }
    public int Position { get; set; }
}
