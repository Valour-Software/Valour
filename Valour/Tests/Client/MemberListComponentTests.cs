using Valour.Client.Components.MembersList;
using Valour.Client.ContextMenu.Menus.Member;
using Valour.Sdk.Client;
using Valour.Sdk.Models;

namespace Valour.Tests.Client;

public class MemberListComponentTests
{
    [Fact]
    public void GetModerationPermissions_WhenCurrentMemberIsUnavailable_DeniesActions()
    {
        var permissions = MemberContextMenu.GetModerationPermissions(null);

        Assert.False(permissions.CanKick);
        Assert.False(permissions.CanBan);
    }

    [Fact]
    public void ShouldHideMember_StaleCurrentUser_RemainsVisible()
    {
        var client = new ValourClient("https://api.valour.example/");
        var user = new User(client)
        {
            Id = 42,
            Name = "Current user",
            TimeLastActive = DateTime.UtcNow.AddDays(-30),
        };
        var member = new PlanetMember(client)
        {
            Id = 100,
            UserId = user.Id,
            User = user,
        };

        Assert.False(MemberListComponent.ShouldHideMember(member, user.Id));
        Assert.True(MemberListComponent.ShouldHideMember(member, user.Id + 1));
    }
}
