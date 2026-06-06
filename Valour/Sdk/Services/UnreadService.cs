using System.Collections.Concurrent;
using Valour.Sdk.Client;
using Valour.Sdk.Nodes;
using Valour.Shared.Models;

namespace Valour.Sdk.Services;

public class UnreadService : ServiceBase
{
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

        _unreadPlanets.Clear();
        foreach (var planetId in result.Data)
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

        _unreadDirectChannels.Clear();
        foreach (var channelId in result.Data)
        {
            _unreadDirectChannels[channelId] = 0;
        }
    }

    public void MarkChannelRead(long? planetId, long channelId)
    {
        if (planetId is null)
        {
            _unreadDirectChannels.TryRemove(channelId, out _);
        }
        else if (_unreadPlanetChannels.TryGetValue(planetId.Value, out var cache))
        {
            cache.TryRemove(channelId, out _);
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
            _unreadDirectChannels[channelId] = 0;
        }
        else
        {
            _unreadPlanets[planetId.Value] = 0;
            var cache = _unreadPlanetChannels.GetOrAdd(planetId.Value, _ => new ConcurrentDictionary<long, byte>());
            cache[channelId] = 0;
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
