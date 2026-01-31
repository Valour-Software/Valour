using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

namespace Valour.Shared.Models;

[StructLayout(LayoutKind.Sequential)]
public readonly struct PlanetRoleMembership : IEquatable<PlanetRoleMembership>
{
    /// <summary>
    /// A default instance of PlanetRoleMembership with only the default role.
    /// </summary>
    public static readonly PlanetRoleMembership Default = new(0x01);
    
    [JsonInclude]
    public readonly long Rf0;
    [JsonInclude]
    public readonly long Rf1;
    [JsonInclude]
    public readonly long Rf2;
    [JsonInclude]
    public readonly long Rf3;
    
    [JsonConstructor]
    public PlanetRoleMembership(long rf0, long rf1 = 0, long rf2 = 0, long rf3 = 0)
    {
        Rf0 = rf0;
        Rf1 = rf1;
        Rf2 = rf2;
        Rf3 = rf3;
    }

    /// <summary>
    /// Returns a read-only span over the four backing fields.
    /// </summary>
    [JsonIgnore]
    public ReadOnlySpan<long> Fields =>
        MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in Rf0), 4);

    public int GetRoleCount() =>
        BitOperations.PopCount((ulong)Rf0) +
        BitOperations.PopCount((ulong)Rf1) +
        BitOperations.PopCount((ulong)Rf2) +
        BitOperations.PopCount((ulong)Rf3);

    public int[] GetRoleIndices()
    {
        int total = GetRoleCount();
        var result = new int[total];
        int index = 0;

        for (int block = 0; block < 4; block++)
        {
            long bits = Fields[block];
            while (bits != 0)
            {
                int tzc = BitOperations.TrailingZeroCount((ulong)bits);
                result[index++] = (block << 6) + tzc;
                bits &= ~(1L << tzc);
            }
        }

        return result;
    }

    public static PlanetRoleMembership FromRoleIndices(IEnumerable<int> roleIndices)
    {
        long rb0 = 0L, rb1 = 0L, rb2 = 0L, rb3 = 0L;
        foreach (var roleId in roleIndices)
        {
            int block = roleId >> 6;
            int offset = roleId & 63;
            switch (block)
            {
                case 0:
                    rb0 |= 1L << offset;
                    break;
                case 1:
                    rb1 |= 1L << offset;
                    break;
                case 2:
                    rb2 |= 1L << offset;
                    break;
                case 3:
                    rb3 |= 1L << offset;
                    break;
            }
        }
        return new PlanetRoleMembership(rb0, rb1, rb2, rb3);
    }

    public bool HasRole(int index)
    {
        if ((uint)index >= 256)
            return false;

        int block = index >> 6;   // localId / 64.
        int offset = index & 63;  // localId % 64.

        return (Fields[block] & (1L << offset)) != 0;
    }
    
    public PlanetRoleMembership AddRole(ISharedPlanetRole role) =>
        AddRoleByIndex(role.FlagBitIndex);

    public PlanetRoleMembership AddRoleByIndex(int roleIndex)
    {
        // Copy the existing fields.
        long rb0 = Rf0, rb1 = Rf1, rb2 = Rf2, rb3 = Rf3;
        
        // Calculate the block and offset.
        int block = roleIndex >> 6;
        int offset = roleIndex & 63;
        
        // Set the bit.
        switch (block)
        {
            case 0:
                rb0 |= 1L << offset;
                break;
            case 1:
                rb1 |= 1L << offset;
                break;
            case 2:
                rb2 |= 1L << offset;
                break;
            case 3:
                rb3 |= 1L << offset;
                break;
        }
        
        return new PlanetRoleMembership(rb0, rb1, rb2, rb3);
    }
    
    public PlanetRoleMembership RemoveRole(ISharedPlanetRole role) =>
        RemoveRoleByIndex(role.FlagBitIndex);
    
    public PlanetRoleMembership RemoveRoleByIndex(int roleIndex)
    {
        // Copy the existing fields.
        long rb0 = Rf0, rb1 = Rf1, rb2 = Rf2, rb3 = Rf3;
        
        // Calculate the block and offset.
        int block = roleIndex >> 6;
        int offset = roleIndex & 63;
        
        // Clear the bit.
        switch (block)
        {
            case 0:
                rb0 &= ~(1L << offset);
                break;
            case 1:
                rb1 &= ~(1L << offset);
                break;
            case 2:
                rb2 &= ~(1L << offset);
                break;
            case 3:
                rb3 &= ~(1L << offset);
                break;
        }
        
        return new PlanetRoleMembership(rb0, rb1, rb2, rb3);
    }
    
    /// <summary>
    /// Allows enumerating the indices of all roles in the membership,
    /// without allocating an array.
    /// </summary>
    public IEnumerable<int> EnumerateRoleIndices()
    {
        for (int block = 0; block < 4; block++)
        {
            var bits = Fields[block];
            while (bits != 0)
            {
                var tzc = BitOperations.TrailingZeroCount((ulong)bits);
                yield return (block << 6) + tzc;
                bits &= ~(1L << tzc);
            }
        }
    }
    
    public byte[] ToBinary()
    {
        var bytes = new byte[32]; // 4 longs * 8 bytes
        var span = bytes.AsSpan();
        BitConverter.TryWriteBytes(span.Slice(0, 8), Rf0);
        BitConverter.TryWriteBytes(span.Slice(8, 8), Rf1);
        BitConverter.TryWriteBytes(span.Slice(16, 8), Rf2);
        BitConverter.TryWriteBytes(span.Slice(24, 8), Rf3);
        return bytes;
    }
    
    public static PlanetRoleMembership FromBinary(byte[] bytes)
    {
        if (bytes.Length < 32) 
            return PlanetRoleMembership.Default;
            
        return new PlanetRoleMembership(
            BitConverter.ToInt64(bytes, 0),
            BitConverter.ToInt64(bytes, 8),
            BitConverter.ToInt64(bytes, 16),
            BitConverter.ToInt64(bytes, 24));
    }

    #region IEquatable<PlanetRoleMembership> Implementation

    /// <summary>
    /// Checks for equality with another PlanetRoleMembership without boxing.
    /// </summary>
    public bool Equals(PlanetRoleMembership other) =>
        Rf0 == other.Rf0 &&
        Rf1 == other.Rf1 &&
        Rf2 == other.Rf2 &&
        Rf3 == other.Rf3;

    /// <summary>
    /// Overrides the default Equals(object) for correct value comparison.
    /// </summary>
    public override bool Equals(object obj) =>
        obj is PlanetRoleMembership other && Equals(other);

    /// <summary>
    /// Combines hash codes for all fields.
    /// </summary>
    public override int GetHashCode() =>
        HashCode.Combine(Rf0, Rf1, Rf2, Rf3);

    public static bool operator ==(PlanetRoleMembership left,
                                     PlanetRoleMembership right) =>
        left.Equals(right);

    public static bool operator !=(PlanetRoleMembership left,
                                     PlanetRoleMembership right) =>
        !(left == right);

    #endregion
}
