namespace Valour.Shared.Models;

/// <summary>
/// A channel a user has favorited. Favorites are per-user and ordered by
/// Position within their planet. Only leaf channels (not categories) may be
/// favorited. No foreign keys reference the channel or planet so favorites
/// work for federated planets whose channels are not in the local database.
/// </summary>
public interface ISharedChannelFavorite : ISharedModel<long>
{
    const string BaseRoute = "api/channelfavorites";

    long UserId { get; set; }
    long ChannelId { get; set; }
    long PlanetId { get; set; }

    /// <summary>
    /// The order of this favorite within the user's favorites for its planet.
    /// Lower values are shown first.
    /// </summary>
    int Position { get; set; }
}
