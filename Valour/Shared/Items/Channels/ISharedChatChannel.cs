namespace Valour.Shared.Items.Channels;

public interface ISharedChatChannel : ISharedChannel
{
    long MessageCount { get; set; }
}
