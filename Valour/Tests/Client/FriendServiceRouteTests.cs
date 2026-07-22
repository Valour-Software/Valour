using Valour.Sdk.Client;
using Valour.Sdk.Models;
using Valour.Sdk.Services;
using Valour.Shared.Models;

namespace Valour.Tests.Client;

public class FriendServiceRouteTests
{
    [Fact]
    public void ApplyFriendData_AttachesBootstrapUsersToClient()
    {
        var client = new ValourClient("https://api.valour.example/");
        var user = new User(null)
        {
            Id = 42,
            Name = "Detached",
            Tag = "0001"
        };

        client.FriendService.ApplyFriendData(new UserFriendData
        {
            Added = [user],
            AddedBy = [user]
        });

        var friend = Assert.Single(client.FriendService.Friends);
        Assert.Same(client, friend.Client);
        Assert.Same(friend, client.FriendService.FriendLookup[user.Id]);
    }

    [Fact]
    public void OnFriendEventReceived_AttachesRealtimeUserToClient()
    {
        var client = new ValourClient("https://api.valour.example/");
        var eventData = new FriendEventData
        {
            User = new User(null)
            {
                Id = 43,
                Name = "Detached",
                Tag = "0002"
            },
            Type = FriendEventType.AddedMe
        };

        client.FriendService.OnFriendEventReceived(eventData);

        var incoming = Assert.Single(client.FriendService.IncomingRequests);
        Assert.Same(client, incoming.Client);
        Assert.Same(incoming, eventData.User);
    }

    [Theory]
    [InlineData("add")]
    [InlineData("decline")]
    [InlineData("remove")]
    [InlineData("cancel")]
    public void BuildFriendActionRoute_EncodesStyledUnicodeNameAndTag(string action)
    {
        const string name = "𝓝𝓸𝓿𝓪(They/Them)";
        const string tag = "星 42";

        var route = FriendService.BuildFriendActionRoute(action, $"{name}#{tag}");

        Assert.Equal(
            $"api/userfriends/{action}ByNameAndTag/" +
            $"{Uri.EscapeDataString(name)}/{Uri.EscapeDataString(tag)}",
            route);
        Assert.DoesNotContain(" ", route);
        Assert.DoesNotContain("/Them", route);
    }
}
