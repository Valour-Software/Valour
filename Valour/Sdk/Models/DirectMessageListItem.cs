using Valour.Sdk.Client;

namespace Valour.Sdk.Models;

public class DirectMessageListItem
{
    public Channel Channel { get; set; }
    public User OtherUser { get; set; }

    public DirectMessageListItem Sync(ValourClient client)
    {
        Channel = Channel?.Sync(client);
        OtherUser = OtherUser?.Sync(client);
        return this;
    }
}
