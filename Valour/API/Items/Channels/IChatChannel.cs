using Valour.Api.Items.Messages;
using Valour.Shared.Items.Channels;

namespace Valour.Api.Items.Channels
{
    public interface IChatChannel : IChannel, ISharedChatChannel
    {
        public Task<List<Message>> GetLastMessagesGenericAsync(int count = 10);
        public Task<List<Message>> GetMessagesGenericAsync(long index = long.MaxValue, int count = 10);
    }
}
