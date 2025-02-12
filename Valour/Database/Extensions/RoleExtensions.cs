using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;

namespace Valour.Database.Extensions;

public class RoleComboInfo
{
    public required long RoleHashKey;
    public required long[] Roles;
}

public static class RoleExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IQueryable<RoleComboInfo> GetUniquePlanetRoleComboInfo(this IQueryable<PlanetRoleMember> roleMembers)
    {
        return roleMembers.GroupBy(rm => rm.Member.RoleMembershipHash)
            .Select(g => new RoleComboInfo
            {
                RoleHashKey = g.Key,
                Roles = g.SelectMany(rm => rm.Member.RoleMembership)
                    .Select(rm => rm.Role.Id)
                    .Distinct()
                    .OrderBy(id => id)
                    .ToArray()
            });
    }
}