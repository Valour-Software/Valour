using Microsoft.AspNetCore.SignalR.Client;
using Valour.Sdk.Client;
using Valour.Sdk.Models.Messages.Embeds;
using Valour.Sdk.Nodes;
using Valour.Shared;
using Valour.Shared.Utilities;

namespace Valour.Sdk.Services;

public class MessageService : ServiceBase
{
    /// <summary>
    /// Run when a message is received
    /// </summary>
    public HybridEvent<Message> MessageReceived;
    
    /// <summary>
    /// Run when a message is edited
    /// </summary>
    public HybridEvent<Message> MessageEdited;

    /// <summary>
    /// Run when a planet is deleted
    /// </summary>
    public HybridEvent<Message> MessageDeleted;
    
    /// <summary>
    /// Run when a personal embed update is received
    /// </summary>
    public HybridEvent<PersonalEmbedUpdate> PersonalEmbedUpdate;

    /// <summary>
    /// Run when a channel embed update is received
    /// </summary>
    public HybridEvent<ChannelEmbedUpdate> ChannelEmbedUpdate;
    
    private readonly LogOptions _logOptions = new(
        "MessageService",
        "#3381a3",
        "#a3333e",
        "#a39433"
    );
    
    private readonly ValourClient _client;
    private readonly CacheService _cache;
    
    public MessageService(ValourClient client)
    {
        _client = client;
        _cache = client.Cache;
        SetupLogging(client.Logger, _logOptions);
        
        _client.NodeService.NodeAdded += HookHubEvents;
    }
    
    public async ValueTask<Message> FetchMessageAsync(long id, bool skipCache = false)
    {
        if (!skipCache && _client.Cache.Messages.TryGet(id, out var cached))
            return cached;
        
        var response = await _client.PrimaryNode.GetJsonAsync<Message>($"api/message/{id}");

        return response.Data.Sync(_client);
    }
    
    public async ValueTask<Message> FetchMessageAsync(long id, Planet planet, bool skipCache = false)
    {
        if (!skipCache && _client.Cache.Messages.TryGet(id, out var cached))
            return cached;
        
        var response = await (planet?.Node ?? _client.PrimaryNode).GetJsonAsync<Message>($"api/message/{id}");

        return response.Data.Sync(_client);
    }
    
    /// <summary>
    /// Sends a message
    /// </summary>
    public Task<TaskResult<Message>> SendMessage(Message message)
        => message.PostAsync();
    
    /// <summary>
    /// Ran when a message is received
    /// </summary>
    private void OnPlanetMessageReceived(Message message)
    {
        var cached = message.Sync(_client);
        
        Log($"[{cached.Node?.Name}]: Received planet message {cached.Id} for channel {cached.ChannelId}");

        MessageReceived?.Invoke(cached);

        if (message.PlanetId is not null)
        {
            if (!_cache.Planets.TryGet(message.PlanetId.Value, out var planet))
            {
                return;
            }
            
            if (planet!.Channels.TryGet(cached.ChannelId, out var channel))
            {
                channel?.NotifyMessageReceived(message);
            }
        }
        else
        {
            if (_cache.Channels.TryGet(cached.ChannelId, out var channel))
            {
                channel?.NotifyMessageReceived(message);
            }
        }
    }
    
    /// <summary>
    /// Ran when a message is edited
    /// </summary>
    private void OnPlanetMessageEdited(Message message)
    {
        Log($"[{message.Node?.Name}]: Received planet message edit {message.Id} for channel {message.ChannelId}");

        var cached = message.Sync(_client);
        
        MessageEdited?.Invoke(cached);
        
        if (message.PlanetId is not null)
        {
            if (!_cache.Planets.TryGet(message.PlanetId.Value, out var planet))
            {
                return;
            }
            
            if (planet!.Channels.TryGet(cached.ChannelId, out var channel))
            {
                channel?.NotifyMessageEdited(message);
            }
        }
        else
        {
            if (_cache.Channels.TryGet(message.ChannelId, out var channel))
            {
                channel?.NotifyMessageEdited(message);
            }
        }
    }
    
    /// <summary>
    /// Ran when a message is recieved
    /// </summary>
    private void OnDirectMessageReceived(Message message)
    {
        Log($"[{message.Node?.Name}]: Received direct message {message.Id} for channel {message.ChannelId}");

        var cached = message.Sync(_client);
        
        MessageReceived?.Invoke(cached);
        
        if (_cache.Channels.TryGet(message.ChannelId, out var channel))
        {
            channel?.NotifyMessageReceived(message);
        }
    }
    
    /// <summary>
    /// Ran when a message is edited
    /// </summary>
    private void OnDirectMessageEdited(Message message)
    {
        Log($"[{message.Node?.Name}]: Received direct message edit {message.Id} for channel {message.ChannelId}");

        var cached = message.Sync(_client);
        
        MessageEdited?.Invoke(cached);
        
        if (_cache.Channels.TryGet(message.ChannelId, out var channel))
        {
            channel?.NotifyMessageEdited(message);
        }
    }

    private void OnMessageDeleted(Message message)
    {
        MessageDeleted?.Invoke(message);
        
        if (message.PlanetId is not null)
        {
            if (!_cache.Planets.TryGet(message.PlanetId.Value, out var planet))
            {
                return;
            }
            
            if (planet!.Channels.TryGet(message.ChannelId, out var channel))
            {
                channel?.NotifyMessageDeleted(message);
            }
        }
        else
        {
            if (_cache.Channels.TryGet(message.ChannelId, out var channel))
            {
                channel?.NotifyMessageDeleted(message);
            }
        }
    }
    
    private void OnPersonalEmbedUpdate(PersonalEmbedUpdate update)
    {
        PersonalEmbedUpdate?.Invoke(update);
    }

    private void OnChannelEmbedUpdate(ChannelEmbedUpdate update)
    {
        ChannelEmbedUpdate?.Invoke(update);
    }
    
    private void HookHubEvents(Node node)
    {
        node.HubConnection.On<Message>("Relay", OnPlanetMessageReceived);
        node.HubConnection.On<Message>("RelayEdit", OnPlanetMessageEdited);
        node.HubConnection.On<Message>("RelayDirect", OnDirectMessageReceived);
        node.HubConnection.On<Message>("RelayDirectEdit", OnDirectMessageEdited);
        node.HubConnection.On<Message>("DeleteMessage", _client.MessageService.OnMessageDeleted);
        node.HubConnection.On<PersonalEmbedUpdate>("Personal-Embed-Update", OnPersonalEmbedUpdate);
        node.HubConnection.On<ChannelEmbedUpdate>("Channel-Embed-Update", OnChannelEmbedUpdate);
    }
}