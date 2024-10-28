using Valour.Sdk.Client;
using Valour.Shared.Models;

namespace Valour.SDK.Services;

public class DmService
{
    private readonly ValourClient _client;
    private readonly CacheService _cache;
    
    public DmService(ValourClient client)
    {
        _client = client;
        _cache = client.Cache;
    }
    
    /// <summary>
    /// Given a user id, returns the direct channel between them and the requester.
    /// If create is true, this will create the channel if it is not found.
    /// </summary>
    public async ValueTask<Channel> FetchDmChannelAsync(long otherUserId, bool create = false, bool skipCache = false)
    {
        var key = new DirectChannelKey(_client.Self.Id, otherUserId);

        if (!skipCache &&
            _cache.DmChannelKeyToId.TryGetValue(key, out var id) &&
            _cache.Channels.TryGet(id, out var cached))
            return cached;

        var dmChannel = (await _client.PrimaryNode.GetJsonAsync<Channel>(
            $"{ISharedChannel.BaseRoute}/direct/{otherUserId}?create={create}")).Data;

        return _cache.Sync(dmChannel);
    }
}