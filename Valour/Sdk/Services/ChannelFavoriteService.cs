using Valour.Sdk.Client;
using Valour.Sdk.Models;
using Valour.Shared;
using Valour.Shared.Models;
using Valour.Shared.Utilities;

namespace Valour.Sdk.Services;

/// <summary>
/// Holds the user's favorite channels, shown as a folder at the top of each
/// planet's channel list. Favorites live on the user's home node, so they
/// work across federated planets.
/// </summary>
public class ChannelFavoriteService : ServiceBase
{
    /// <summary>
    /// Fired whenever the favorites list changes in any way.
    /// </summary>
    public HybridEvent FavoritesChanged;

    private readonly ValourClient _client;
    private readonly List<ChannelFavorite> _favorites = new();

    public readonly IReadOnlyList<ChannelFavorite> Favorites;

    public ChannelFavoriteService(ValourClient client)
    {
        _client = client;
        Favorites = _favorites;
        SetupLogging(client.Logger, new LogOptions("ChannelFavoriteService", "#3381a3", "#a3333e", "#a39433"));
    }

    public async Task LoadFavoritesAsync()
    {
        var response = await _client.PrimaryNode.GetJsonAsync<List<ChannelFavorite>>("api/users/me/channelfavorites");
        if (!response.Success)
        {
            LogError($"Failed to load channel favorites: {response.Message}");
            return;
        }

        ApplyFavorites(response.Data);
    }

    public void ApplyFavorites(IEnumerable<ChannelFavorite> favorites)
    {
        _favorites.Clear();
        _favorites.AddRange((favorites ?? []).Select(x => x.Sync(_client)));
        SortFavorites();
        FavoritesChanged?.Invoke();
    }

    public bool IsFavorite(long channelId) =>
        _favorites.Any(x => x.ChannelId == channelId);

    /// <summary>
    /// Returns the planet's favorited channels in favorite order, skipping
    /// favorites whose channel no longer exists (or is somehow a category).
    /// </summary>
    public List<Channel> GetFavoriteChannels(Planet planet)
    {
        var channels = new List<Channel>();
        foreach (var favorite in _favorites)
        {
            if (favorite.PlanetId != planet.Id)
                continue;

            var channel = planet.Channels.Get(favorite.ChannelId);
            if (channel is null || channel.ChannelType == ChannelTypeEnum.PlanetCategory)
                continue;

            channels.Add(channel);
        }

        return channels;
    }

    public async Task<TaskResult> AddFavoriteAsync(Channel channel)
    {
        if (channel.PlanetId is null)
            return TaskResult.FromFailure("Only planet channels can be favorited.");

        if (channel.ChannelType == ChannelTypeEnum.PlanetCategory)
            return TaskResult.FromFailure("Categories cannot be favorited.");

        if (IsFavorite(channel.Id))
            return TaskResult.SuccessResult;

        var favorite = new ChannelFavorite(_client)
        {
            ChannelId = channel.Id,
            PlanetId = channel.PlanetId.Value
        };

        var response = await _client.PrimaryNode.PostAsyncWithResponse<ChannelFavorite>(
            ISharedChannelFavorite.BaseRoute, favorite);
        if (!response.Success)
            return response.WithoutData();

        _favorites.Add(response.Data.Sync(_client));
        SortFavorites();
        FavoritesChanged?.Invoke();

        return TaskResult.SuccessResult;
    }

    public async Task<TaskResult> RemoveFavoriteAsync(long channelId)
    {
        var response = await _client.PrimaryNode.DeleteAsync(
            $"{ISharedChannelFavorite.BaseRoute}/by-channel/{channelId}");
        if (!response.Success)
            return response;

        _favorites.RemoveAll(x => x.ChannelId == channelId);
        FavoritesChanged?.Invoke();

        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// Applies the given channel order to the planet's favorites, updating
    /// locally first so the sidebar responds instantly.
    /// </summary>
    public async Task<TaskResult> ReorderAsync(long planetId, List<long> orderedChannelIds)
    {
        var position = 0;
        foreach (var channelId in orderedChannelIds)
        {
            var favorite = _favorites.FirstOrDefault(x => x.PlanetId == planetId && x.ChannelId == channelId);
            if (favorite is not null)
                favorite.Position = position++;
        }

        foreach (var favorite in _favorites.Where(x => x.PlanetId == planetId).OrderBy(x => x.Position).ToList())
        {
            if (!orderedChannelIds.Contains(favorite.ChannelId))
                favorite.Position = position++;
        }

        SortFavorites();
        FavoritesChanged?.Invoke();

        var request = new ReorderChannelFavoritesRequest
        {
            PlanetId = planetId,
            ChannelIds = orderedChannelIds
        };

        var response = await _client.PrimaryNode.PostAsync($"{ISharedChannelFavorite.BaseRoute}/order", request);
        if (!response.Success)
        {
            // Local order may now disagree with the server - resync.
            LogError($"Failed to save favorite order: {response.Message}");
            await LoadFavoritesAsync();
        }

        return response;
    }

    private void SortFavorites()
    {
        _favorites.Sort((a, b) =>
        {
            var planetCompare = a.PlanetId.CompareTo(b.PlanetId);
            return planetCompare != 0 ? planetCompare : a.Position.CompareTo(b.Position);
        });
    }
}
