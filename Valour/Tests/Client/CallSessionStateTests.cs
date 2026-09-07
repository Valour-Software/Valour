using Valour.Client.Components.Calls;
using Valour.Sdk.Client;
using Valour.Sdk.Models;

namespace Valour.Tests.Client;

public class CallSessionStateTests
{
    [Fact]
    public async Task LostTransport_ClearsMediaAndParticipantsButRetainsReconnectTarget()
    {
        var client = new ValourClient("http://localhost:59999/");
        await using var session = new GlobalCallSessionService(client, new RealtimeKitHostService());
        var channel = new Channel(client) { Id = 42 };
        typeof(GlobalCallSessionService).GetProperty(nameof(session.ActiveChannel))!.SetValue(session, channel);
        typeof(GlobalCallSessionService).GetProperty(nameof(session.Joined))!.SetValue(session, true);
        typeof(GlobalCallSessionService).GetProperty(nameof(session.VideoEnabled))!.SetValue(session, true);
        session.ApplyParticipantsSnapshot(new() { ConnectionState = "connected", Participants = [new() { PeerId = "peer" }] });
        Assert.Single(session.ParticipantsSnapshot!.Participants);

        session.ApplyParticipantsSnapshot(new() { ConnectionState = "disconnected" });

        Assert.False(session.Joined);
        Assert.False(session.AudioEnabled);
        Assert.False(session.VideoEnabled);
        Assert.Null(session.ParticipantsSnapshot);
        Assert.Same(channel, session.ActiveChannel);
        Assert.Contains("Reconnect", session.Error);
        await session.LeaveAsync(clearChannel: true);
        Assert.Null(session.ActiveChannel);
        Assert.Null(session.Error);
    }
}
