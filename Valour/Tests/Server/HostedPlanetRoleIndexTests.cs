using Valour.Server.Models;
using Valour.Server.Utilities;
using Valour.Shared.Authorization;

namespace Valour.Tests.Server;

public class HostedPlanetRoleIndexTests
{
    [Fact]
    public void UpsertRole_WithChangedIndex_ReleasesOldSlot()
    {
        var planet = new Planet { Id = 1, Name = "Index test" };
        var role = new PlanetRole { Id = 10, PlanetId = 1, Name = "Role", FlagBitIndex = 3, Position = 1 };
        var hosted = new HostedPlanet(planet, new(), new() { role }, new(), new());

        Assert.Equal(10, hosted.GetRoleIdByIndex(3));

        hosted.UpsertRole(new PlanetRole { Id = 10, PlanetId = 1, Name = "Role", FlagBitIndex = 4, Position = 1 });

        Assert.Equal(0, hosted.GetRoleIdByIndex(3));
        Assert.Null(hosted.GetRoleByIndex(3));
        Assert.Equal(10, hosted.GetRoleIdByIndex(4));
    }

    [Fact]
    public void UpsertRole_WithChangedIndex_KeepsSlotClaimedByAnotherRole()
    {
        var planet = new Planet { Id = 1, Name = "Index test" };
        var first = new PlanetRole { Id = 10, PlanetId = 1, Name = "First", FlagBitIndex = 3, Position = 1 };
        var hosted = new HostedPlanet(planet, new(), new() { first }, new(), new());

        // Another role claims slot 3 after the first role has moved away from it
        hosted.UpsertRole(new PlanetRole { Id = 10, PlanetId = 1, Name = "First", FlagBitIndex = 5, Position = 1 });
        hosted.UpsertRole(new PlanetRole { Id = 11, PlanetId = 1, Name = "Second", FlagBitIndex = 3, Position = 2 });
        hosted.UpsertRole(new PlanetRole { Id = 10, PlanetId = 1, Name = "First", FlagBitIndex = 6, Position = 1 });

        Assert.Equal(11, hosted.GetRoleIdByIndex(3));
        Assert.Equal(0, hosted.GetRoleIdByIndex(5));
        Assert.Equal(10, hosted.GetRoleIdByIndex(6));
    }
}

public class PermissionGrantGuardTests
{
    [Fact]
    public void ChangedNodeBits_IgnoresUnsetCodeBits()
    {
        // Code bits outside the mask have no effect, so changing them is not a change
        Assert.Equal(0, PermissionGrantGuard.GetChangedNodeBits(0x0, 0x0, 0x4, 0x0));

        // Setting a deny (mask only) and an allow (mask and code) both count as changes
        Assert.Equal(0x1, PermissionGrantGuard.GetChangedNodeBits(0x0, 0x0, 0x0, 0x1));
        Assert.Equal(0x2, PermissionGrantGuard.GetChangedNodeBits(0x0, 0x0, 0x2, 0x2));

        // Flipping a set bit from allow to deny is a change
        Assert.Equal(0x2, PermissionGrantGuard.GetChangedNodeBits(0x2, 0x2, 0x0, 0x2));
    }

    [Fact]
    public void UnheldChangeError_NamesMissingPermissions()
    {
        var held = PlanetPermissions.Kick.Value;
        var changed = PlanetPermissions.Kick.Value | PlanetPermissions.Ban.Value;

        var error = PermissionGrantGuard.GetUnheldChangeError(changed, held, PlanetPermissions.Permissions, "planet");

        Assert.NotNull(error);
        Assert.Contains(PlanetPermissions.Ban.Name, error);
        Assert.DoesNotContain(PlanetPermissions.Kick.Name, error);
        Assert.Null(PermissionGrantGuard.GetUnheldChangeError(PlanetPermissions.Kick.Value, held,
            PlanetPermissions.Permissions, "planet"));
    }
}
