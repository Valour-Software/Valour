using Valour.Client.Components.Menus.Modals.Channels.Edit;
using Valour.Sdk.Models;

namespace Valour.Tests.Client;

public class ChannelPermissionsComponentTests
{
    [Fact]
    public void SelectFirstEditableRole_WhenNoRolesExist_ReturnsNull()
    {
        var result = EditCLIPermissionsComponent.SelectFirstEditableRole([], 10);

        Assert.Null(result);
    }

    [Fact]
    public void SelectFirstEditableRole_SkipsRolesAtOrAboveMemberAuthority()
    {
        var tooPowerful = new PlanetRole(null)
        {
            Id = 1,
            Name = "Too powerful",
            Position = 0
        };
        var editable = new PlanetRole(null)
        {
            Id = 2,
            Name = "Editable",
            Position = uint.MaxValue
        };

        var result = EditCLIPermissionsComponent.SelectFirstEditableRole(
            [tooPowerful, editable],
            memberAuthority: 10);

        Assert.Same(editable, result);
    }

    [Fact]
    public void SelectFirstEditableRole_WhenMemberCannotEditAnyRole_ReturnsNull()
    {
        var role = new PlanetRole(null)
        {
            Id = 1,
            Name = "Peer role",
            Position = 0
        };

        var result = EditCLIPermissionsComponent.SelectFirstEditableRole(
            [role],
            memberAuthority: role.GetAuthority());

        Assert.Null(result);
    }
}
