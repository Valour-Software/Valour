using System.Collections.Concurrent;
using Valour.Shared.Items.Channels;
using Valour.Shared.Items.Messages;

namespace Valour.Server.Services;

public class ChannelStateService
{
    /// <summary>
    /// Locally cached channel states
    /// </summary>
    private static readonly ConcurrentDictionary<long, long?> ChannelMessageStates = new();

    public static string GetState(long channelId)
    {
        ChannelMessageStates.TryGetValue(channelId, out var messageState);
        if (messageState is null)
            return string.Empty;

        return messageState.ToString();
    }

    public static void SetMessageState(ISharedChannel channel, ISharedMessage message)
    {
        ChannelMessageStates[channel.Id] = message.Id;
    }
}