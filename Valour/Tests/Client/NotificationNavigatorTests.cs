using Valour.Client.Utility;
using Valour.Shared.Models;

namespace Valour.Tests.Client;

public class NotificationNavigatorTests
{
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
