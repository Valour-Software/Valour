
namespace Valour.Shared.Planets
{
    public enum ChannelListItemType
    {
        ChatChannel,
        Category
    }

    public class ChannelListItem
    {
        public ushort Position { get; set; }

        public ulong? Parent_Id { get; set;}

        public ulong Planet_Id { get; set; }

        public ulong Id { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }
    }
}