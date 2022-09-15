using Valour.Api.Items.Messages;
using Valour.Shared.Items.Channels;

namespace Valour.Api.Items.Channels
{
    public interface IChatChannel<TMessage> : IChannel, ISharedChatChannel
    {
        public Task<List<TMessage>> GetLastMessagesAsync(int count = 10);
    }
}
