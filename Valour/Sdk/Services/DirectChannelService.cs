using Valour.Sdk.Client;

namespace Valour.SDK.Services;

public class DirectChannelService
{
    private readonly ValourClient _client;
    
    /// <summary>
    /// The direct chat channels (dms) of this user
    /// </summary>
    public IReadOnlyList<Channel> DirectChatChannels { get; private set; }
    private readonly List<Channel> _directChatChannels = new();
    
    /// <summary>
    /// Lookup for direct chat channels
    /// </summary>
    public IReadOnlyDictionary<long, Channel> DirectChatChannelsLookup { get; private set; }
    private readonly Dictionary<long, Channel> _directChatChannelsLookup = new();
    
    public DirectChannelService(ValourClient client)
    {
        _client = client;
        
        DirectChatChannels = _directChatChannels;
        DirectChatChannelsLookup = _directChatChannelsLookup;
    }
    
    public async Task LoadDirectChatChannelsAsync()
    {
        var response = await _client.PrimaryNode.GetJsonAsync<List<Channel>>("api/channels/direct/self");
        if (!response.Success)
        {
            Console.WriteLine("** Failed to load direct chat channels **");
            Console.WriteLine(response.Message);

            return;
        }
        
        // Clear existing
        _directChatChannels.Clear();
        _directChatChannelsLookup.Clear();
        
        foreach (var channel in response.Data)
        {
            // Custom cache insert behavior
            if (channel.Members is not null && channel.Members.Count > 0)
            {
                var id0 = channel.Members[0].Id;
                
                // Self channel
                if (channel.Members.Count == 1)
                {
                    var key = new DirectChannelKey(id0, id0);
                    Channel.DirectChannelIdLookup.Add(key, channel.Id);
                }
                // Other channel
                else if (channel.Members.Count == 2)
                {
                    var id1 = channel.Members[1].Id;
                    var key = new DirectChannelKey(id0, id1);
                    Channel.DirectChannelIdLookup.Add(key, channel.Id);
                }
            }

            var cached = channel.Sync();
            _directChatChannels.Add(cached);
            _directChatChannelsLookup.Add(cached.Id, cached);
        }
        
        Console.WriteLine($"Loaded {DirectChatChannels.Count} direct chat channels...");
    }
}