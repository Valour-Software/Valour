using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;

namespace Valour.Database.Extensions;

public static class RoleExtensions
{
    public static IQueryable<PlanetMember> WithRoleByLocalId(this IQueryable<PlanetMember> members, long planetId, int localRoleId)
    {

        long searchKey;
        
        // expression for different search keys base on bit position
        switch (localRoleId)
        {
            case < 64:
                searchKey = 1L << localRoleId;
                return members.Where(x => x.PlanetId == planetId && (x.Rf0 & searchKey) != 0);
            case < 128:
                searchKey = 1L << (localRoleId - 64);
                return members.Where(x => x.PlanetId == planetId && (x.Rf1 & searchKey) != 0);
            case < 192:
                searchKey = 1L << (localRoleId - 128);
                return members.Where(x => x.PlanetId == planetId && (x.Rf2 & searchKey) != 0);
            default:
                searchKey = 1L << (localRoleId - 192);
                return members.Where(x => x.PlanetId == planetId && (x.Rf3 & searchKey) != 0);
        }
    }
    
    public static async Task<int> SetRoleFlag(this IQueryable<PlanetMember> members, int localRoleId, bool value)
    {
        long mask;

        // Set bit to 1
        if (value)
        {
            // expression for different masks base on bit position
            switch (localRoleId)
            {
                case < 64:
                    mask = 1L << localRoleId;
                    return await members.ExecuteUpdateAsync(x => x.SetProperty(p => p.Rf0, p => p.Rf0 | mask));
                case < 128:
                    mask = 1L << (localRoleId - 64);
                    return await members.ExecuteUpdateAsync(x => x.SetProperty(p => p.Rf1, p => p.Rf1 | mask));
                case < 192:
                    mask = 1L << (localRoleId - 128);
                    return await members.ExecuteUpdateAsync(x => x.SetProperty(p => p.Rf2, p => p.Rf2 | mask));
                default:
                    mask = 1L << (localRoleId - 192);
                    return await members.ExecuteUpdateAsync(x => x.SetProperty(p => p.Rf3, p => p.Rf3 | mask));
            }
        }
        // Set bit to 0
        else
        {
            // expression for different masks base on bit position
            switch (localRoleId)
            {
                case < 64:
                    mask = ~(1L << localRoleId);
                    return await members.ExecuteUpdateAsync(x => x.SetProperty(p => p.Rf0, p => p.Rf0 & mask));
                case < 128:
                    mask = ~(1L << (localRoleId - 64));
                    return await members.ExecuteUpdateAsync(x => x.SetProperty(p => p.Rf1, p => p.Rf1 & mask));
                case < 192:
                    mask = ~(1L << (localRoleId - 128));
                    return await members.ExecuteUpdateAsync(x => x.SetProperty(p => p.Rf2, p => p.Rf2 & mask));
                default:
                    mask = ~(1L << (localRoleId - 192));
                    return await members.ExecuteUpdateAsync(x => x.SetProperty(p => p.Rf3, p => p.Rf3 & mask));
            }
        }
    }
}