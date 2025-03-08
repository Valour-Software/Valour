using System.Runtime.CompilerServices;

namespace Valour.Shared.Models;

/// <summary>
/// A wrapper for a channel position that provides utility methods
/// </summary>
public struct ChannelPosition
{
    public uint RawPosition { get; init; }
    public uint Depth { get; init; }
    public uint LocalPosition { get; init; }
    
    public ChannelPosition(uint rawPosition)
    {
        RawPosition = rawPosition;
        Depth = GetDepth(rawPosition);
        LocalPosition = GetLocalPosition(rawPosition, Depth);
    }
    
    // Okay, I know what you're thinking: Why do these methods just call back to
    // the static methods? Why not just implement it in the instance?
    // The answer is that the database project needs to use the same
    // implementations, and the database will not understand the struct instance.
    // Thus, the actual implementations are static.
    
    public ChannelPosition Append(uint relativePosition)
    {
        if (Depth >= 4)
            throw new InvalidOperationException("Cannot append to a channel with a depth of 4");
        
        return new ChannelPosition(AppendRelativePosition(RawPosition, relativePosition));
    }

    public ChannelPosition GetParentPosition()
    {
        return new ChannelPosition(GetParentPosition(RawPosition));
    }
    
    public uint GetDirectChildMask()
    {
        return GetDirectChildMaskByDepth(Depth);
    }
    
    ////////////////////
    // Static Methods //
    ////////////////////
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint GetDepth(ISharedChannel channel) => GetDepth(channel.RawPosition);

    public static uint GetDepth(uint position)
    {
        // Planet case
        if (position == 0)
            return 0;
    
        // Check bytes from least significant to most significant
        if ((position & 0x000000FFu) != 0) return 4; // Fourth byte is set
        if ((position & 0x0000FF00u) != 0) return 3; // Third byte is set
        if ((position & 0x00FF0000u) != 0) return 2; // Second byte is set
        return 1; // Must be just in the first byte
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint GetLocalPosition(ISharedChannel channel) => GetLocalPosition(channel.RawPosition);
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint GetLocalPosition(uint position)
    {
        var depth = GetDepth(position);
        // use depth to determine amount to shift
        var shift = 8 * (4 - depth);
        var shifted = position >> (int)shift;
        // now clear the higher bits
        return shifted & 0xFFu;
    }
    
    // Overload for if you already know the depth
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint GetLocalPosition(uint position, uint depth)
    {
        // use depth to determine amount to shift
        var shift = 8 * (4 - depth);
        var shifted = position >> (int)shift;
        // now clear the higher bits
        return shifted & 0xFFu;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint AppendRelativePosition(uint parentPosition, uint relativePosition)
    {
        var depth = GetDepth(parentPosition);
        return AppendRelativePosition(parentPosition, relativePosition, depth + 1);
    }
    
    // Overload for if you already know the depth
    // This version explicitly takes the target depth (where the result should be)
    public static uint AppendRelativePosition(uint parentPosition, uint relativePosition, uint targetDepth)
    {
        // For depth 1: shift by 24 bits (position in first byte)
        // For depth 2: shift by 16 bits (position in second byte)  
        // For depth 3: shift by 8 bits (position in third byte)
        // For depth 4: shift by 0 bits (position in fourth byte)
        var shift = 8 * (4 - targetDepth);
    
        var shifted = relativePosition << (int)shift;
        return parentPosition | shifted;
    }
    
    /// <summary>
    /// Returns the bounds of the descendants of a channel
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static (uint lower, uint upper) GetDescendentBounds(uint parentPosition)
    {
        var depth = GetDepth(parentPosition);
        return GetDescendentBounds(parentPosition, depth);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static (uint lower, uint upper) GetDescendentBounds(uint parentPosition, uint depth)
    {
        var lower = GetLowerBound(parentPosition, depth);
        var upper = GetUpperBound(parentPosition, depth);
        
        return (lower, upper);
    }
    
    // This one is quite easy
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint GetLowerBound(uint parentPosition)
    {
        var depth = GetDepth(parentPosition);
        return GetLowerBound(parentPosition, depth);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint GetLowerBound(uint parentPosition, uint depth)
    {
        return AppendRelativePosition(parentPosition, 1, depth + 1);
    }
    
    // This one is a bit more complex
    // We cannot just append because we need to cover all of the bytes past the depth
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint GetUpperBound(uint parentPosition)
    {
        var depth = GetDepth(parentPosition);
        return GetUpperBound(parentPosition, depth);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint GetUpperBound(uint parentPosition, uint depth)
    {
        if (depth >= 4)
            return parentPosition; // No descendants beyond depth 4
    
        // Create a mask with FA in all positions after current depth
        // For depth 0: 0xFAFAFAFA (all four bytes)
        // For depth 1: 0x00FAFAFA (bytes 2,3,4)
        // For depth 2: 0x0000FAFA (bytes 3,4)
        // For depth 3: 0x000000FA (byte 4)
        uint upperBoundMask = 0u;
    
        for (int i = (int)depth + 1; i <= 4; i++)
        {
            upperBoundMask |= (uint)0xFA << (8 * (4 - i));
        }
    
        return parentPosition | upperBoundMask;
    }
    
    /// <summary>
    /// Given a depth, returns a bit mask that can be used to get the direct children of a channel
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint GetDirectChildMaskByDepth(uint depth)
    {
        // For depth 0: Return 0xFF000000 (match first byte only)
        // For depth 1: Return 0x00FF0000 (match second byte only)
        // For depth 2: Return 0x0000FF00 (match third byte only)
        // For depth 3: Return 0x000000FF (match fourth byte only)
        return (uint)0xFF << (int)(8 * (3 - depth));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint GetParentPosition(uint position)
    {
        var depth = GetDepth(position);
        return GetParentPosition(position, depth);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint GetParentPosition(uint position, uint depth)
    {
        if (depth < 2)
            return 0;
        
        var shift = 8 * (depth - 1);
        
        // shift to right then invert to avoid long
        return position & ~(0xFFFFFFFFu >> (int)shift);
    }
    
    /// <summary>
    /// Returns all ancestor positions, from immediate parent to root level
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ChannelPosition[] GetAncestorPositions(ChannelPosition position)
    {
        var depth = GetDepth(position.RawPosition);
        if (depth <= 1)
            return []; // No ancestors for planet or top-level channels
        
        // We'll have depth-1 ancestors (e.g., depth 4 has 3 ancestors)
        var ancestors = new ChannelPosition[depth - 1];
    
        uint currentPos = position.RawPosition;
        for (uint i = 0; i < depth - 1; i++)
        {
            // Get the parent of the current position
            currentPos = GetParentPosition(currentPos);
            ancestors[i] = new ChannelPosition(currentPos);
        }
    
        return ancestors;
    }

    /// <summary>
    /// Returns all ancestor positions, from immediate parent to root level
    /// </summary>
    public ChannelPosition[] GetAncestorPositions()
    {
        return GetAncestorPositions(this);
    }

}