namespace Valour.Shared.Channels;
public interface ISharedUserChannelState
{
    long ChannelId { get; set; }
    long UserId { get; set; }
    string LastViewedState { get; set; }
}
