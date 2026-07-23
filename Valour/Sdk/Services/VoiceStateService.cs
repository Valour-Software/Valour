using Microsoft.AspNetCore.SignalR.Client;
using Valour.Sdk.Client;
using Valour.Sdk.Nodes;
using Valour.Shared.Models;
using Valour.Shared.Utilities;

namespace Valour.Sdk.Services;

public class VoiceStateService : ServiceBase
{
    /// <summary>
    /// Fired when the participant list for any channel changes.
    /// Argument is the channel ID that changed.
    /// </summary>
    public HybridEvent<long> VoiceParticipantsChanged;

    private readonly Dictionary<long, HashSet<long>> _channelParticipants = new();
    private readonly ValourClient _client;

    private readonly LogOptions _logOptions = new(
        "VoiceStateService",
        "#6a5acd",
        "#a3333e",
        "#a39433"
    );

    public VoiceStateService(ValourClient client)
    {
        _client = client;
        SetupLogging(client.Logger, _logOptions);
        _client.NodeService.NodeAdded += HookHubEvents;
    }

    public void SetInitialVoiceState(Dictionary<long, List<long>>? voiceParticipants)
    {
        if (voiceParticipants is null)
            return;

        foreach (var kvp in voiceParticipants)
        {
            _channelParticipants[kvp.Key] = new HashSet<long>(kvp.Value);
        }
    }

    public List<long> GetParticipants(long channelId)
    {
        if (_channelParticipants.TryGetValue(channelId, out var set))
            return set.ToList();

        return new List<long>();
    }

    public int GetParticipantCount(long channelId)
    {
        if (_channelParticipants.TryGetValue(channelId, out var set))
            return set.Count;

        return 0;
    }

    private void HookHubEvents(Node node)
    {
        node.HubConnection.On<VoiceChannelParticipantsUpdate>(
            "Voice-Channel-Participants",
            update =>
            {
                if (node.AcceptsExternalPlanetRealtimeEvent(update?.PlanetId))
                    OnVoiceChannelParticipantsUpdate(update);
            });
    }

    private void OnVoiceChannelParticipantsUpdate(VoiceChannelParticipantsUpdate update)
    {
        if (update is null)
            return;

        if (update.UserIds is null || update.UserIds.Count == 0)
        {
            _channelParticipants.Remove(update.ChannelId);
        }
        else
        {
            _channelParticipants[update.ChannelId] = new HashSet<long>(update.UserIds);
        }

        VoiceParticipantsChanged?.Invoke(update.ChannelId);
    }
}
