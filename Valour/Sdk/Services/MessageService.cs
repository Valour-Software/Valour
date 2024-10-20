using Valour.Shared;
using Valour.Shared.Utilities;

namespace Valour.SDK.Services;

public static class MessageService
{
    /// <summary>
    /// Run when a message is received
    /// </summary>
    public static HybridEvent<Message> MessageReceived;
    
    /// <summary>
    /// Run when a message is edited
    /// </summary>
    public static HybridEvent<Message> MessageEdited;

    /// <summary>
    /// Run when a planet is deleted
    /// </summary>
    public static HybridEvent<Message> MessageDeleted;
    
    /// <summary>
    /// Sends a message
    /// </summary>
    public static async Task<TaskResult> SendMessage(Message message)
        => await message.PostMessageAsync();
    
    /// <summary>
    /// Ran when a message is recieved
    /// </summary>
    public static void HandlePlanetMessageReceived(Message message)
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
    public static void HandlePlanetMessageEdited(Message message)
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
    public static void HandleDirectMessageReceived(Message message)
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
    public static void HandleDirectMessageEdited(Message message)
    {
        Console.WriteLine($"[{message.Node?.Name}]: Received direct message edit {message.Id} for channel {message.ChannelId}");
        if (message.ReplyTo is not null)
        {
            message.ReplyTo = message.ReplyTo.Sync();
        }
        
        var cached = message.Sync();
        
        MessageEdited?.Invoke(cached);
    }

    public static void HandleMessageDeleted(Message message)
    {
        MessageDeleted?.Invoke(message);
    }
}