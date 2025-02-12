namespace Valour.Shared.Authorization;

public static class PlanetPermissionUtils
{
    public const long Seed = unchecked((long)0xcbf29ce484222325); // FNV1a seed
    public const long MagicNumber = unchecked((long)0x9e3779b97f4a7c15);
    
    private static long MixHash(long currentHash, long roleId)
    {
        return currentHash ^ ((roleId + MagicNumber) + (currentHash << 6) + (currentHash >> 2));
    }

    public static long GetRoleChannelComboKey(long rolesKey, long channelId)
    {
        var hash = MixHash(Seed, channelId);
        hash = MixHash(rolesKey, hash);
        return hash;
    }

    public static long GenerateRoleMembershipHash(long[] sortedRoleIds)
    {
        long hash = Seed;
        foreach (var roleId in sortedRoleIds)
            hash = MixHash(hash, roleId);
        return hash;
    }
}