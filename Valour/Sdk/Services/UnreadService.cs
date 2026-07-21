using System.Collections.Concurrent;
using Valour.Sdk.Client;
using Valour.Sdk.Nodes;
using Valour.Shared.Models;
using Valour.Shared.Utilities;

namespace Valour.Sdk.Services;

public class UnreadService : ServiceBase
{
    /// <summary>
    /// Run when a planet's unread state changes. Argument is the planet id.
    /// </summary>
    public HybridEvent<long> PlanetUnreadChanged;

    /// <summary>
    /// Run when a direct channel's unread state changes. Argument is the channel id.
    /// </summary>
    public HybridEvent<long> DirectChannelUnreadChanged;

    private readonly LogOptions _logOptions = new(
        "UnreadService",
        "#3381a3",
        "#a3333e",
        "#a39433"
    );

    private readonly ValourClient _client;

    private readonly ConcurrentDictionary<long, byte> _unreadPlanets = new();
    private readonly ConcurrentDictionary<long, ConcurrentDictionary<long, byte>> _unreadPlanetChannels = new();
    private readonly ConcurrentDictionary<long, byte> _unreadDirectChannels = new();
    
    public UnreadService(ValourClient client)
    {
        _client = client;
        SetupLogging(_client.Logger, _logOptions);
    }

    public async Task FetchUnreadPlanetsAsync()
    {
        var result = await _client.PrimaryNode.GetJsonAsync<long[]>($"api/unread/planets");
        if (!result.Success)
        {
            LogError($"Failed to fetch unread planets: {result.Message}");
            return;
        }

        ApplyUnreadPlanets(result.Data);
    }

    public void ApplyUnreadPlanets(IEnumerable<long> planetIds)
    {

        _unreadPlanets.Clear();
        foreach (var planetId in planetIds ?? [])
        {
            _unreadPlanets[planetId] = 0;
        }
    }

    public async Task FetchUnreadPlanetChannelsAsync(long planetId)
    {
        var result = await _client.PrimaryNode.GetJsonAsync<long[]>($"api/unread/planets/{planetId}/channels");
        if (!result.Success)
        {
            LogError($"Failed to fetch unread channels for planet {planetId}: {result.Message}");
            return;
        }

        var cache = _unreadPlanetChannels.GetOrAdd(planetId, _ => new ConcurrentDictionary<long, byte>());
        cache.Clear();

        foreach (var channelId in result.Data)
        {
            cache[channelId] = 0;
        }
    }

    public async Task FetchUnreadDirectChannelsAsync()
    {
        var result = await _client.PrimaryNode.GetJsonAsync<long[]>($"api/unread/direct/channels");

        if (!result.Success)
        {
            LogError($"Failed to fetch unread direct channels: {result.Message}");
            return;
        }

        ApplyUnreadDirectChannels(result.Data);
    }

    public void ApplyUnreadDirectChannels(IEnumerable<long> channelIds)
    {

        _unreadDirectChannels.Clear();
        foreach (var channelId in channelIds ?? [])
        {
            _unreadDirectChannels[channelId] = 0;
        }
    }

    public void MarkChannelRead(long? planetId, long channelId)
    {
        if (planetId is null)
        {
            if (_unreadDirectChannels.TryRemove(channelId, out _))
            {
                DirectChannelUnreadChanged?.Invoke(channelId);
            }
        }
        else if (_unreadPlanetChannels.TryGetValue(planetId.Value, out var cache))
        {
            cache.TryRemove(channelId, out _);

            // If that was the last unread channel, the planet itself is now read
            if (cache.IsEmpty && _unreadPlanets.TryRemove(planetId.Value, out _))
            {
                PlanetUnreadChanged?.Invoke(planetId.Value);
            }
        }

        Channel? channel = null;

        // Get the channel
        if (planetId is not null && _client.Cache.Planets.TryGet(planetId.Value, out var planet))
        {
            planet!.Channels.TryGet(channelId, out channel);
        }
        else
        {
            _client.Cache.Channels.TryGet(channelId, out channel);
        }

        // If we found the channel, mark it as read
        channel?.MarkUnread(false);
    }

    public void MarkChannelUnread(long? planetId, long channelId)
    {
        if (planetId is null)
        {
            if (_unreadDirectChannels.TryAdd(channelId, 0))
            {
                DirectChannelUnreadChanged?.Invoke(channelId);
            }
        }
        else
        {
            var becameUnread = _unreadPlanets.TryAdd(planetId.Value, 0);
            var cache = _unreadPlanetChannels.GetOrAdd(planetId.Value, _ => new ConcurrentDictionary<long, byte>());
            cache[channelId] = 0;

            if (becameUnread)
            {
                PlanetUnreadChanged?.Invoke(planetId.Value);
            }
        }

        Channel? channel = null;

        if (planetId is not null && _client.Cache.Planets.TryGet(planetId.Value, out var planet))
        {
            planet!.Channels.TryGet(channelId, out channel);
        }
        else
        {
            _client.Cache.Channels.TryGet(channelId, out channel);
        }

        channel?.MarkUnread(true);
    }

    public bool IsPlanetUnread(long planetId) => _unreadPlanets.ContainsKey(planetId);

    public bool IsChannelUnread(long? planetId, long channelId)
    {
        if (planetId is null)
        {
            return _unreadDirectChannels.ContainsKey(channelId);
        }

        return _unreadPlanetChannels.TryGetValue(planetId.Value, out var cache) && cache.ContainsKey(channelId);
    }
}
