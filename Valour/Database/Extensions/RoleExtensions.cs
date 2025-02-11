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
        return from rm in roleMembers
            group rm by rm.Member.RoleHashKey into g
            select new RoleComboInfo
            {
                RoleHashKey = g.Key,
                Roles = g.SelectMany(x => x.Member.RoleMembership
                        .Select(r => r.Role.Id))
                    .Distinct()
                    .OrderBy(x => x)
                    .ToArray()
            };
    }
}