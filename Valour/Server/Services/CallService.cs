#nullable enable

using System.Collections.Concurrent;
using System.Net.Http.Headers;
using Valour.Config.Configs;
using Valour.Shared;
using Valour.Shared.Models.Calls;

namespace Valour.Server.Services;

public class CallService
{
    private readonly CoreHubService _hubService;
    private readonly ILogger<CallService> _logger;
    
    private readonly ConcurrentDictionary<long, LiveCall> _channelToCall = new();
    private readonly ConcurrentDictionary<long, LiveCall> _userToCall = new();

    private const string CfCallsApiPrefix = "https://rtc.live.cloudflare.com/v1/apps/";
    private const string CfTurnApiPrefix = "https://rtc.live.cloudflare.com/v1/turn/keys";

    private readonly string CfRequestTurnRoute;
    private readonly HttpClient _cfTurnHttpClient;
    
    public CallService(CoreHubService hubService, ILogger<CallService> logger)
    {
        _hubService = hubService;
        _logger = logger;
        
        if (CloudflareConfig.Current is null)
        {
            _logger.LogError("Cloudflare config is not set up, calls will not work");
            return;
        }
        
        if (string.IsNullOrWhiteSpace(CloudflareConfig.Current.TurnToken))
        {
            _logger.LogError("Cloudflare TURN token is not set up, calls will not work");
            return;
        }

        if (string.IsNullOrWhiteSpace(CloudflareConfig.Current.TurnTokenId))
        {
            _logger.LogError("Cloudflare TURN token ID is not set up, calls will not work");
            return;
        }
        
        CfRequestTurnRoute = $"{CfTurnApiPrefix}/{CloudflareConfig.Current.TurnTokenId}/credentials/generate";
        _cfTurnHttpClient = new HttpClient();
        _cfTurnHttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CloudflareConfig.Current.TurnToken);
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
    
    public async Task<IEnumerable<LiveCallParticipant>> GetCallParticipantsAsync(long channelId)
    {
        if (!_channelToCall.TryGetValue(channelId, out var call))
        {
            return new List<LiveCallParticipant>();
        }

        return call.Participants.Snapshot;
    }
}