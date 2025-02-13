using System.Numerics;

namespace Valour.Shared.Models;

public readonly struct MemberRoleFlags
{
    // roles 0-63
    public readonly long Rf0;

    // roles 64-127
    public readonly long Rf1;

    // roles 128-191
    public readonly long Rf2;

    // roles 192-255
    public readonly long Rf3;

    public MemberRoleFlags(long rf0, long rf1, long rf2, long rf3)
    {
        Rf0 = rf0;
        Rf1 = rf1;
        Rf2 = rf2;
        Rf3 = rf3;
    }
    
    public int GetRoleCount()
    {
        var count = 0;
        count += BitOperations.PopCount((ulong)Rf0);
        count += BitOperations.PopCount((ulong)Rf1);
        count += BitOperations.PopCount((ulong)Rf2);
        count += BitOperations.PopCount((ulong)Rf3);
        return count;
    }

    public List<int> GetRoleIds()
    {
        var roleIds = new List<int>();

        AddRoleIds(Rf0, 0, roleIds);
        AddRoleIds(Rf1, 64, roleIds);
        AddRoleIds(Rf2, 128, roleIds);
        AddRoleIds(Rf3, 192, roleIds);

        return roleIds;
    }

    private static void AddRoleIds(long rb, int offset, List<int> roleIds)
    {
        if (rb == 0)
            return;

        var count = BitOperations.PopCount((ulong)rb);
        if (count == 0)
            return;

        for (int i = 0; i < 64 && count > 0; i++)
        {
            if ((rb & (1L << i)) != 0)
            {
                roleIds.Add((i + offset));
                count--;
            }
        }
    }
    
    public static MemberRoleFlags FromRoleIds(IEnumerable<int> roleIds)
    {
        var rb0 = 0L;
        var rb1 = 0L;
        var rb2 = 0L;
        var rb3 = 0L;

        foreach (var roleId in roleIds)
        {
            if (roleId < 64)
                rb0 |= 1L << roleId;
            else if (roleId < 128)
                rb1 |= 1L << (roleId - 64);
            else if (roleId < 192)
                rb2 |= 1L << (roleId - 128);
            else if (roleId < 256)
                rb3 |= 1L << (roleId - 192);
        }

        return new MemberRoleFlags(rb0, rb1, rb2, rb3);
    }
}