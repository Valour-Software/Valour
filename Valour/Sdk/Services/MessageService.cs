using Microsoft.AspNetCore.SignalR.Client;
using Valour.Sdk.Client;
using Valour.Sdk.Models.Messages.Embeds;
using Valour.Sdk.Nodes;
using Valour.Shared;
using Valour.Shared.Utilities;

namespace Valour.SDK.Services;

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
    
    /// <summary>
    /// Sends a message
    /// </summary>
    public async Task<TaskResult> SendMessage(Message message)
        => await message.PostMessageAsync();
    
    /// <summary>
    /// Ran when a message is recieved
    /// </summary>
    public void OnPlanetMessageReceived(Message message)
    {
        Log($"[{message.Node?.Name}]: Received planet message {message.Id} for channel {message.ChannelId}");
        if (message.ReplyTo is not null)
        {
            message.ReplyTo = _cache.Sync(message.ReplyTo);
        }
        
        var cached = _cache.Sync(message);

        MessageReceived?.Invoke(cached);
    }
    
    /// <summary>
    /// Ran when a message is edited
    /// </summary>
    public void OnPlanetMessageEdited(Message message)
    {
        Log($"[{message.Node?.Name}]: Received planet message edit {message.Id} for channel {message.ChannelId}");
        if (message.ReplyTo is not null)
        {
            message.ReplyTo = _cache.Sync(message.ReplyTo);
        }

        var cached = _cache.Sync(message);
        
        MessageEdited?.Invoke(cached);
    }
    
    /// <summary>
    /// Ran when a message is recieved
    /// </summary>
    public void OnDirectMessageReceived(Message message)
    {
        Log($"[{message.Node?.Name}]: Received direct message {message.Id} for channel {message.ChannelId}");
        
        if (message.ReplyTo is not null)
        {
            message.ReplyTo = _cache.Sync(message.ReplyTo);
        }
        
        var cached = _cache.Sync(message);
        
        MessageReceived?.Invoke(cached);
    }
    
    /// <summary>
    /// Ran when a message is edited
    /// </summary>
    public void OnDirectMessageEdited(Message message)
    {
        Log($"[{message.Node?.Name}]: Received direct message edit {message.Id} for channel {message.ChannelId}");
        if (message.ReplyTo is not null)
        {
            message.ReplyTo = _cache.Sync(message.ReplyTo);
        }
        
        var cached = _cache.Sync(message);
        
        MessageEdited?.Invoke(cached);
    }

    public void OnMessageDeleted(Message message)
    {
        MessageDeleted?.Invoke(message);
    }
    
    public void OnPersonalEmbedUpdate(PersonalEmbedUpdate update)
    {
        PersonalEmbedUpdate?.Invoke(update);
    }

    public void OnChannelEmbedUpdate(ChannelEmbedUpdate update)
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