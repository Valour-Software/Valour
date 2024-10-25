using Valour.Sdk.Client;
using Valour.Shared;
using Valour.Shared.Utilities;

namespace Valour.SDK.Services;

public class MessageService
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
    
    private readonly ValourClient _client;
    
    public MessageService(ValourClient client)
    {
        _client = client;
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
        Console.WriteLine($"[{message.Node?.Name}]: Received planet message {message.Id} for channel {message.ChannelId}");
        if (message.ReplyTo is not null)
        {
            message.ReplyTo = message.ReplyTo.Sync();
        }
        
        var cached = message.Sync();

        MessageReceived?.Invoke(cached);
    }
    
    /// <summary>
    /// Ran when a message is edited
    /// </summary>
    public void OnPlanetMessageEdited(Message message)
    {
        Console.WriteLine($"[{message.Node?.Name}]: Received planet message edit {message.Id} for channel {message.ChannelId}");
        if (message.ReplyTo is not null)
        {
            message.ReplyTo = message.ReplyTo.Sync();
        }

        var cached = message.Sync();
        
        MessageEdited?.Invoke(cached);
    }
    
    /// <summary>
    /// Ran when a message is recieved
    /// </summary>
    public void OnDirectMessageReceived(Message message)
    {
        Console.WriteLine($"[{message.Node?.Name}]: Received direct message {message.Id} for channel {message.ChannelId}");
        
        if (message.ReplyTo is not null)
        {
            message.ReplyTo = message.ReplyTo.Sync();
        }
        
        var cached = message.Sync();
        
        MessageReceived?.Invoke(cached);
    }
    
    /// <summary>
    /// Ran when a message is edited
    /// </summary>
    public void OnDirectMessageEdited(Message message)
    {
        Console.WriteLine($"[{message.Node?.Name}]: Received direct message edit {message.Id} for channel {message.ChannelId}");
        if (message.ReplyTo is not null)
        {
            message.ReplyTo = message.ReplyTo.Sync();
        }
        
        var cached = message.Sync();
        
        MessageEdited?.Invoke(cached);
    }

    public void OnMessageDeleted(Message message)
    {
        MessageDeleted?.Invoke(message);
    }
}