namespace Valour.Shared.Channels
{
    public class ChannelWatchingUpdate
    {
        public long? PlanetId { get; set; }
        public long ChannelId { get; set; }
        public List<long> UserIds { get; set; }
    }
}
