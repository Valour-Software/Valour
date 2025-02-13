using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Valour.Shared.Models;

// Ensure fields are laid out sequentially
[StructLayout(LayoutKind.Sequential)]
public readonly struct PlanetRoleMembership
{
    // Roles 0–63.
    public readonly long Rf0;
    // Roles 64–127.
    public readonly long Rf1;
    // Roles 128–191.
    public readonly long Rf2;
    // Roles 192–255.
    public readonly long Rf3;

    public PlanetRoleMembership(long rf0, long rf1 = 0, long rf2 = 0, long rf3 = 0)
    {
        Rf0 = rf0;
        Rf1 = rf1;
        Rf2 = rf2;
        Rf3 = rf3;
    }

    /// <summary>
    /// Returns a read-only span over the four backing fields.
    /// This property is very lightweight and inlined by the JIT.
    /// </summary>
    public ReadOnlySpan<long> Fields =>
        MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in Rf0), 4);

    public int GetRoleCount() =>
      BitOperations.PopCount((ulong)Rf0) +
      BitOperations.PopCount((ulong)Rf1) +
      BitOperations.PopCount((ulong)Rf2) +
      BitOperations.PopCount((ulong)Rf3);

    /// <summary>
    /// Returns an array of all local role IDs present in this membership.
    /// Uses the span to iterate over the backing fields.
    /// </summary>
    public int[] GetLocalRoleIds()
    {
        // First, determine the total number of set bits.
        int total = GetRoleCount();
        var result = new int[total];
        int index = 0;

        // Iterate across the four fields (blocks 0,1,2,3)
        for (int block = 0; block < 4; block++)
        {
            // Retrieve the field from the span.
            long bits = Fields[block];
            // While there are bits set in this field...
            while (bits != 0)
            {
                // Find the least-significant set bit.
                int tzc = BitOperations.TrailingZeroCount((ulong)bits);
                // Compute the global localId: block * 64 + offset.
                result[index++] = (block << 6) + tzc;
                // Reset that bit.
                bits &= ~(1L << tzc);
            }
        }

        return result;
    }

    /// <summary>
    /// Creates a new instance from an enumerable of local role IDs.
    /// Uses the span idea to distribute bits into the four fields.
    /// </summary>
    public static PlanetRoleMembership FromLocalRoleIds(IEnumerable<int> roleIds)
    {
        long rb0 = 0L, rb1 = 0L, rb2 = 0L, rb3 = 0L;
        foreach (var roleId in roleIds)
        {
            // Use bit shifts instead of subtraction.
            int block = roleId >> 6;      // divide by 64
            int offset = roleId & 63;       // modulo 64
            switch (block)
            {
                case 0: rb0 |= 1L << offset; break;
                case 1: rb1 |= 1L << offset; break;
                case 2: rb2 |= 1L << offset; break;
                case 3: rb3 |= 1L << offset; break;
            }
        }
        return new PlanetRoleMembership(rb0, rb1, rb2, rb3);
    }

    /// <summary>
    /// Determines if a given local role is set.
    /// Uses a safe, branchless approach with a span over the backing fields.
    /// </summary>
    public bool HasRole(int localId)
    {
        if ((uint)localId >= 256)
            return false;

        int block = localId >> 6;   // localId / 64
        int offset = localId & 63;    // localId % 64

        return (Fields[block] & (1L << offset)) != 0;
    }
}
