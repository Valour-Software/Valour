namespace Valour.Server.Models;

public class DirectMessageListItem
{
    public Channel Channel { get; set; }
    public User OtherUser { get; set; }
    public List<User> Users { get; set; } = [];
    public string DisplayName { get; set; }
}
