using System.Collections.Concurrent;
using Valour.Server.Database;
using Valour.Server.Database.Items.Channels;
using Valour.Shared.Items.Channels;
using Valour.Shared.Items.Messages;

namespace Valour.Server.Services;

public class ChannelStateService
{
    private readonly ValourDB _db;

    public ChannelStateService(ValourDB db)
    {
        _db = db;
    }
    
    /// <summary>
    /// Locally cached channel states
    /// </summary>
    private static readonly ConcurrentDictionary<long, long?> ChannelMessageStates = new();

    public async Task<string> GetState(long channelId)
    {
        ChannelMessageStates.TryGetValue(channelId, out var messageState);
        if (messageState is null)
        {
            messageState = await _db.PlanetMessages.OrderByDescending(x => x.TimeSent).Select(x => x.Id).FirstOrDefaultAsync();
        }

        return messageState.ToString();
    }

    public async Task SetMessageState(Channel channel, long messageId)
    {
        ChannelMessageStates[channel.Id] = messageId;

        channel.State = messageId.ToString();
        channel.TimeLastActive = DateTime.UtcNow;

        _db.Channels.Update(channel);
        await _db.SaveChangesAsync();
    }
}