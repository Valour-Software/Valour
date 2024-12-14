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
        // This is the planet
        if (position == 0)
            return 0;
        
        // Check if the third and fourth bytes (depth 3 and 4) are present
        if ((position & 0x0000FFFFu) == 0)
        {
            // If they are not, we must be in the first or second layer
            if ((position & 0x00FF0000u) == 0)
            {
                // If the second byte is also zero, it's in the first layer (top level)
                return 1;
            }
            // Otherwise, it's in the second layer
            return 2;
        }
        else
        {
            // Check the lowest byte first (fourth layer)
            if ((position & 0x000000FFu) == 0)
            {
                // If the fourth byte is zero, it's in the third layer
                return 3;
            }
            
            // If none of the previous checks matched, it’s in the fourth layer
            return 4;
        }   
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
        return AppendRelativePosition(parentPosition, relativePosition, depth);
    }
    
    // Overload for if you already know the depth
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint AppendRelativePosition(uint parentPosition, uint relativePosition, uint depth)
    {
        // use depth to determine amount to shift
        var shift = 8 * (3 - depth);
        // shift the relative position to the correct position
        var shifted = relativePosition << (int)shift;
        // now add the relative position to the parent position
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
        return AppendRelativePosition(parentPosition, 1, depth);
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
        var maxed = 0xFAFAFAFAu; // literally means 250-250-250-250
        // maxed gets shifted right by 8 * (4 - depth)
        var shift = 8 * depth; // so if depth = 1, shift = one byte, maxed = 0-250-250-250
        var shifted = maxed >> (int)shift;
        
        return parentPosition | shifted;
    }
    
    /// <summary>
    /// Given a depth, returns a bit mask that can be used to get the direct children of a channel
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint GetDirectChildMaskByDepth(uint depth)
    {
        return (0xFFFFFFFFu >> (int)((depth + 1) * 8));
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

}