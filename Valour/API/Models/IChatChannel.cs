using Valour.Shared.Models;

namespace Valour.Api.Models
{
    public interface IChatChannel : IChannel, ISharedChatChannel
    {
        public Task<List<MessageTransferData<Message>>> GetLastMessagesGenericAsync(int count = 10);
        public Task<List<MessageTransferData<Message>>> GetMessagesGenericAsync(long index = long.MaxValue, int count = 10);
        public Task SendIsTyping();
    }
}
