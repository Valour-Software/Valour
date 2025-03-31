#nullable enable

using System.Collections.Concurrent;
using Valour.Shared;
using Valour.Shared.Models.Calls;

namespace Valour.Server.Services;

public class CallService
{
    private readonly CoreHubService _hubService;
    
    private readonly ConcurrentDictionary<long, LiveCall> _channelToCall = new();
    private readonly ConcurrentDictionary<long, LiveCall> _userToCall = new();
    
    public CallService(CoreHubService hubService)
    {
        _hubService = hubService;
    }
    
    public async Task<LiveCall> GetOrCreateCallAsync(long channelId)
    {
        var call = _channelToCall.GetOrAdd(channelId, _ => new LiveCall());
        if (!call.Initialized)
        {
            _channelToCall[channelId] = call;
            call.Initialized = true;
        }
        
        return call;
    }

    public async Task EndCallAsync(long callId)
    {
        if (!_channelToCall.TryGetValue(callId, out var call))
        {
            return;
        }
        
        // Remove the call from the channel map
        _channelToCall.TryRemove(callId, out _);
        
        // Handle end of call logic
    }

    public async Task<TaskResult<LiveCallModel>> JoinPlanetLiveCallAsync(Channel channel, PlanetMember member)
    {
        if (_userToCall.ContainsKey(member.UserId))
        {
            await LeaveCallAsync(member.UserId);
        }

        return TaskResult<LiveCallModel>.FromData(null);
    }

    public async Task<TaskResult> LeaveCallAsync(long userId)
    {
        _userToCall.TryRemove(userId, out var call);
        if (call is null)
            return TaskResult.SuccessResult;
        
        call.Participants.RemoveAll(x => x.UserId == userId);
        
        if (call.Participants.Count == 0) // Nobody left in the call, end it
        {
            await EndCallAsync(call.ChannelId);
        }

        return TaskResult.SuccessResult;
    }
}