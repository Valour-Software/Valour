using Valour.Api.Client;
using Valour.Api.Items.Channels.Users;
using Valour.Api.Nodes;
using Valour.Shared;
using Valour.Shared.Items.Messages;

namespace Valour.Api.Items.Messages;

public class DirectMessage : Message, ISharedDirectMessage
{
    public static async ValueTask<PlanetMessage> FindAsync(long id, long channelId, bool refresh = false)
    {
        if (!refresh)
        {
            var cached = ValourCache.Get<PlanetMessage>(id);
            if (cached is not null)
                return cached;
        }
        var response = await ValourClient.PrimaryNode.GetJsonAsync<PlanetMessage>($"api/{nameof(DirectChatChannel)}/{channelId}/message/{id}");
        var item = response.Data;

        if (item is not null)
        {
            await ValourCache.Put(id, item);
        }

        return item;
    }

    public override Task<TaskResult> DeleteAsync() =>
        Node.DeleteAsync($"api/{nameof(DirectChatChannel)}/{ChannelId}/messages/{Id}");

    public override ValueTask<string> GetAuthorColorAsync() =>
        ValueTask.FromResult("#ffffff");

    public override async ValueTask<string> GetAuthorImageUrlAsync() =>
        (await GetAuthorUserAsync()).PfpUrl;

    public override async ValueTask<string> GetAuthorNameAsync() =>
        (await GetAuthorUserAsync()).Name;

    public override async ValueTask<string> GetAuthorTagAsync() =>
        (await GetAuthorUserAsync()).Bot ? "Bot" : "User";

    public override async ValueTask<Message> GetReplyMessageAsync()
    {
        if (ReplyToId is null)
            return null;

        return await FindAsync(Id, ChannelId);
    }

    public override Task<TaskResult> PostMessageAsync() =>
        ValourClient.PrimaryNode.PostAsync($"api/{nameof(DirectChatChannel)}/{ChannelId}/messages", this);
}
