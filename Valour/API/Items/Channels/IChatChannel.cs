using Valour.Api.Items.Messages;
using Valour.Shared.Items.Channels;

namespace Valour.Api.Items.Channels
{
    public interface IChatChannel<TMessage> : ISharedChatChannel where TMessage : Message
    {
        public string Name { get; set; }

        public string Description { get; set; }

        public Task<List<TMessage>> GetLastMessagesAsync(int count = 10);
    }
}
