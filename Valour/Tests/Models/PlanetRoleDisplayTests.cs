using Valour.Server.Models;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Tests.Models;

public class PlanetRoleDisplayTests
{
    private static PlanetRole MakeRole(long permissions, bool isAdmin) => new()
    {
        Name = "Test",
        Permissions = permissions,
        IsAdmin = isAdmin
    };

    [Fact]
    public void HasDisplayRole_AdminWithoutFlag_IsFalse()
    {
        // Regression for #1510: HasPermission's IsAdmin bypass made admin roles
        // always "display" even with the flag off; the cosmetic flag must be
        // read raw
        var role = MakeRole(permissions: 0, isAdmin: true);

        Assert.True(((ISharedPlanetRole)role).HasPermission(PlanetPermissions.DisplayRole));
        Assert.False(ISharedPlanetRole.GetHasDisplayRole(role));
    }

    [Fact]
    public void HasDisplayRole_FlagSet_IsTrue()
    {
        var role = MakeRole(permissions: PlanetPermissions.DisplayRole.Value, isAdmin: false);
        Assert.True(ISharedPlanetRole.GetHasDisplayRole(role));
    }

    [Fact]
    public void HasDisplayRole_FullControl_IsTrue()
    {
        var role = MakeRole(permissions: Permission.FULL_CONTROL, isAdmin: false);
        Assert.True(ISharedPlanetRole.GetHasDisplayRole(role));
    }

    [Fact]
    public void HasDisplayRole_FlagClear_IsFalse()
    {
        var role = MakeRole(permissions: PlanetPermissions.Default & ~PlanetPermissions.DisplayRole.Value, isAdmin: false);
        Assert.False(ISharedPlanetRole.GetHasDisplayRole(role));
    }
}
