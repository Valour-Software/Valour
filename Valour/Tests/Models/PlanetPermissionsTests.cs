using Valour.Shared.Authorization;

namespace Valour.Tests.Models;

public class PlanetPermissionsTests
{
    [Fact]
    public void PermissionBits_AreUnique()
    {
        var seen = new HashSet<long>();
        foreach (var permission in PlanetPermissions.Permissions)
        {
            if (permission.Value == Permission.FULL_CONTROL)
                continue;

            Assert.True(seen.Add(permission.Value),
                $"Duplicate permission bit 0x{permission.Value:X} ({permission.Name})");
        }
    }

    [Fact]
    public void PermissionBits_AreSingleBits()
    {
        foreach (var permission in PlanetPermissions.Permissions)
        {
            if (permission.Value == Permission.FULL_CONTROL)
                continue;

            // Exactly one bit set
            Assert.True((permission.Value & (permission.Value - 1)) == 0,
                $"Permission {permission.Name} value 0x{permission.Value:X} is not a single bit");
        }
    }

    [Fact]
    public void ManageWebhooks_IsRegistered()
    {
        Assert.Equal(0x200000, PlanetPermissions.ManageWebhooks.Value);
        Assert.Contains(PlanetPermissions.Permissions, x => x.Value == PlanetPermissions.ManageWebhooks.Value);

        // Not part of the default member grant
        Assert.False(Permission.HasPermission(PlanetPermissions.Default, PlanetPermissions.ManageWebhooks));
    }
}
