using Valour.Client.Utility;
using Valour.Shared.Models;

namespace Valour.Tests.Client;

public class NotificationNavigatorTests
{
    // #1665: "xx messages from xx people" activity notifications used Source
    // = ChannelActivity, which NavigateTo's switch didn't recognize, so
    // clicking "View" always hit the default case ("This notification
    // doesn't have a destination.") instead of opening the channel.
    [Theory]
    [InlineData(NotificationSource.ChannelActivity)]
    [InlineData(NotificationSource.PlanetMemberMention)]
    [InlineData(NotificationSource.PlanetRoleMention)]
    [InlineData(NotificationSource.PlanetMemberReply)]
    [InlineData(NotificationSource.PlanetHereMention)]
    [InlineData(NotificationSource.PlanetEveryoneMention)]
    public void IsPlanetChannelRouteSource_ForPlanetChannelSources_ReturnsTrue(NotificationSource source)
    {
        Assert.True(NotificationNavigator.IsPlanetChannelRouteSource(source));
    }

    [Theory]
    [InlineData(NotificationSource.DirectMention)]
    [InlineData(NotificationSource.DirectReply)]
    [InlineData(NotificationSource.DirectMessage)]
    [InlineData(NotificationSource.ThreadComment)]
    [InlineData(NotificationSource.ThreadReply)]
    [InlineData(NotificationSource.FriendRequest)]
    [InlineData(NotificationSource.FriendRequestAccepted)]
    [InlineData(NotificationSource.EventReminder)]
    [InlineData(NotificationSource.Platform)]
    public void IsPlanetChannelRouteSource_ForNonPlanetChannelSources_ReturnsFalse(NotificationSource source)
    {
        Assert.False(NotificationNavigator.IsPlanetChannelRouteSource(source));
    }
}
