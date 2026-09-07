using Valour.Sdk.Client;

namespace Valour.Sdk.Models;

public class DirectMessageListItem
{
    public Channel Channel { get; set; }
    public User OtherUser { get; set; }
    public List<User> Users { get; set; } = [];
    public string DisplayName { get; set; }

    public DirectMessageListItem Sync(ValourClient client)
    {
        Channel = Channel?.Sync(client);
        OtherUser = OtherUser?.Sync(client);
        Users = Users?.Select(x => x.Sync(client)).ToList() ?? [];
        return this;
    }
}
