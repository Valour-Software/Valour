namespace Valour.Shared.Models;

/// <summary>
/// Sets the order of a user's favorite channels within one planet.
/// ChannelIds is the full desired order; favorites not listed keep their
/// relative order after the listed ones.
/// </summary>
public class ReorderChannelFavoritesRequest
{
    public long PlanetId { get; set; }
    public List<long> ChannelIds { get; set; } = new();
}
