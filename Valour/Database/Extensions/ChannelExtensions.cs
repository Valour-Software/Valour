using System.Runtime.CompilerServices;
using Valour.Shared.Models;

namespace Valour.Database.Extensions;

public static class ChannelExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IQueryable<Channel> InPlanetById(this IQueryable<Channel> channels, long? planetId)
    {
        return channels.Where(x => x.PlanetId == planetId);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IQueryable<Channel> InParentById(this IQueryable<Channel> channels, long? parentId)
    {
        return channels.Where(x => x.ParentId == parentId);
    }
    
    
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IQueryable<Channel> DirectChildrenOf(this IQueryable<Channel> channels, ISharedChannel channel)
    {
        // This one is faster and simpler
        return DirectChildrenOfById(channels, channel.PlanetId, channel.Id);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IQueryable<Channel> DirectChildrenOfById(this IQueryable<Channel> channels, long? planetId, long parentId)
    {
        return channels.Where(x => x.PlanetId == planetId && x.ParentId == parentId);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IQueryable<Channel> DescendantsOf(this IQueryable<Channel> channels, ISharedChannel channel)
    {
        return DescendantsOf(channels, channel.PlanetId, channel.RawPosition);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IQueryable<Channel> DescendantsOf(this IQueryable<Channel> channels, long? planetId, uint position)
    {
        // In this case we return nothing
        if (planetId is null)
            return channels.Where(x => false);
        
        var bounds = ChannelPosition.GetDescendentBounds(position);
        return channels.Where(x =>
            x.PlanetId == planetId &&
            x.RawPosition >= bounds.lower && 
            x.RawPosition < bounds.upper);
    }
}