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
    
    private readonly HashSet<long> _unreadPlanets = new();
    private readonly Dictionary<long, HashSet<long>> _unreadPlanetChannels = new();
    private readonly HashSet<long> _unreadDirectChannels = new();
    
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
            _unreadPlanets.Add(planetId);
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

        if (_unreadPlanetChannels.TryGetValue(planetId, out var cache))
        {
            cache.Clear();
        }
        else
        {
            cache = new HashSet<long>();
            _unreadPlanetChannels[planetId] = cache;
        }
        
        foreach (var channelId in result.Data)
        {
            cache.Add(channelId);
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
            _unreadDirectChannels.Add(channelId);
        }
    }

    public void MarkChannelRead(long? planetId, long channelId)
    {
        if (planetId is null)
        {
            _unreadDirectChannels.Remove(channelId);
        }
        else if (_unreadPlanetChannels.TryGetValue(planetId.Value, out var cache))
        {
            cache.Remove(channelId);
        }
    }
    
    public bool IsPlanetUnread(long planetId) => _unreadPlanets.Contains(planetId);
    
    public bool IsChannelUnread(long? planetId, long channelId)
    {
        if (planetId is null)
        {
            return _unreadDirectChannels.Contains(channelId);
        }
        
        return _unreadPlanetChannels.TryGetValue(planetId.Value, out var cache) && cache.Contains(channelId);
    }
}