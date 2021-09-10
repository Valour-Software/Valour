using System;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Valour.Server.Categories;
using Valour.Server.Database;
using Valour.Server.Planets;
using Valour.Shared.Items;

namespace Valour.Server.Planets
{
    public interface IServerChannelListItem
    {
        [JsonInclude]
        [JsonPropertyName("Id")]
        public ulong Id { get; set; }

        [JsonInclude]
        [JsonPropertyName("Planet_Id")]
        public ulong Planet_Id { get; set; }

        [JsonInclude]
        [JsonPropertyName("Parent_Id")]
        public ulong? Parent_Id { get; set; }

        [JsonInclude]
        [JsonPropertyName("Position")]
        public ushort Position { get; set; }

        [JsonInclude]
        [JsonPropertyName("ItemType")]
        public ItemType ItemType { get; }

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
}