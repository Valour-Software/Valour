
using System.Text.Json.Serialization;
using Valour.Shared.Items;

namespace Valour.Shared.Items
{
    public interface IChannelListItem : IItem, INamedItem
    {
        [JsonInclude]
        public ushort Position { get; set; }

        [JsonInclude]
        public ulong? Parent_Id { get; set;}

        [JsonInclude]
        public ulong Planet_Id { get; set; }

        [JsonInclude]
        public string Description { get; set; }
    }
}