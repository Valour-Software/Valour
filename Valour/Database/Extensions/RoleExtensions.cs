using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Valour.Shared.Models;

namespace Valour.Database.Extensions;
public static class RoleExtensions
{
    public static IQueryable<PlanetMember> WithRoleByLocalIndex(this IQueryable<PlanetMember> members, long planetId, int localRoleId)
    {

        long searchKey;
        
        // expression for different search keys base on bit position
        switch (localRoleId)
        {
            case < 64:
                searchKey = 1L << localRoleId;
                return members.Where(x => x.PlanetId == planetId && (x.RoleMembership.Rf0 & searchKey) != 0);
            case < 128:
                searchKey = 1L << (localRoleId - 64);
                return members.Where(x => x.PlanetId == planetId && (x.RoleMembership.Rf1 & searchKey) != 0);
            case < 192:
                searchKey = 1L << (localRoleId - 128);
                return members.Where(x => x.PlanetId == planetId && (x.RoleMembership.Rf2 & searchKey) != 0);
            default:
                searchKey = 1L << (localRoleId - 192);
                return members.Where(x => x.PlanetId == planetId && (x.RoleMembership.Rf3 & searchKey) != 0);
        }
    }
    
    public static async Task<int> BulkSetRoleFlag(this IQueryable<PlanetMember> members, long planetId, int roleIndex, bool value)
    {
        long mask;

        // Set bit to 1
        if (value)
        {
            // expression for different masks base on bit position
            switch (roleIndex)
            {
                case < 64:
                    mask = 1L << roleIndex;
                    return await members.Where(x => x.PlanetId == planetId).ExecuteUpdateAsync(x => x.SetProperty(p => p.RoleMembership.Rf0, p => p.RoleMembership.Rf0 | mask));
                case < 128:
                    mask = 1L << (roleIndex - 64);
                    return await members.Where(x => x.PlanetId == planetId).ExecuteUpdateAsync(x => x.SetProperty(p => p.RoleMembership.Rf1, p => p.RoleMembership.Rf1 | mask));
                case < 192:
                    mask = 1L << (roleIndex - 128);
                    return await members.Where(x => x.PlanetId == planetId).ExecuteUpdateAsync(x => x.SetProperty(p => p.RoleMembership.Rf2, p => p.RoleMembership.Rf2 | mask));
                default:
                    mask = 1L << (roleIndex - 192);
                    return await members.Where(x => x.PlanetId == planetId).ExecuteUpdateAsync(x => x.SetProperty(p => p.RoleMembership.Rf3, p => p.RoleMembership.Rf3 | mask));
            }
        }
        // Set bit to 0
        else
        {
            // expression for different masks base on bit position
            switch (roleIndex)
            {
                case < 64:
                    mask = ~(1L << roleIndex);
                    return await members.Where(x => x.PlanetId == planetId).ExecuteUpdateAsync(x => x.SetProperty(p => p.RoleMembership.Rf0, p => p.RoleMembership.Rf0 & mask));
                case < 128:
                    mask = ~(1L << (roleIndex - 64));
                    return await members.Where(x => x.PlanetId == planetId).ExecuteUpdateAsync(x => x.SetProperty(p => p.RoleMembership.Rf1, p => p.RoleMembership.Rf1 & mask));
                case < 192:
                    mask = ~(1L << (roleIndex - 128));
                    return await members.Where(x => x.PlanetId == planetId).ExecuteUpdateAsync(x => x.SetProperty(p => p.RoleMembership.Rf2, p => p.RoleMembership.Rf2 & mask));
                default:
                    mask = ~(1L << (roleIndex - 192));
                    return await members.Where(x => x.PlanetId == planetId).ExecuteUpdateAsync(x => x.SetProperty(p => p.RoleMembership.Rf3, p => p.RoleMembership.Rf3 & mask));
            }
        }
    }
}