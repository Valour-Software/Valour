using Valour.Sdk.Services;

namespace Valour.Tests.Client;

public class FriendServiceRouteTests
{
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
