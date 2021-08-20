using Valour.Server.Categories;
using Valour.Server.Database;
using Valour.Shared.Items;

namespace Valour.Server.Planets;

public interface IServerChannelListItem : IChannelListItem
{
    public static async Task<IServerChannelListItem> FindAsync(ItemType type, ulong id, ValourDB db)
    {
        switch (type)
        {
            case ItemType.Channel:
                return await ServerPlanetChatChannel.FindAsync(id, db);
            case ItemType.Category:
                return await ServerPlanetCategory.FindAsync(id, db);
            default:
                throw new ArgumentOutOfRangeException(nameof(ItemType));
        }
    }

    public void NotifyClientsChange();
}
