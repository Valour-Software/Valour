using Microsoft.AspNetCore.SignalR.Client;
using Valour.Sdk.Client;
using Valour.Sdk.Nodes;
using Valour.Shared;
using Valour.Shared.Models;
using Valour.Shared.Utilities;

namespace Valour.Sdk.Services;

public sealed class DirectCallService : ServiceBase
{
    private readonly ValourClient _client;
    private readonly Dictionary<long, DirectCall> _currentCalls = [];

    public IReadOnlyDictionary<long, DirectCall> CurrentCalls => _currentCalls;
    public HybridEvent<DirectCall> CallUpdated;

    public DirectCallService(ValourClient client)
    {
        _client = client;
        client.NodeService.NodeAdded += HookHubEvents;
    }

    public async Task LoadCurrentAsync()
    {
        var result = await _client.PrimaryNode.GetJsonAsync<List<DirectCall>>("api/direct-calls/current");
        if (!result.Success || result.Data is null)
            return;

        _currentCalls.Clear();
        foreach (var call in result.Data)
            Apply(call);
    }

    public Task<TaskResult<DirectCall>> StartAsync(long channelId, DirectCallKind kind) =>
        SendAsync("api/direct-calls", new StartDirectCallRequest { ChannelId = channelId, Kind = kind });

    public Task<TaskResult<DirectCall>> AcceptAsync(long callId) =>
        SendAsync($"api/direct-calls/{callId}/accept", null);

    public Task<TaskResult<DirectCall>> DeclineAsync(long callId) =>
        SendAsync($"api/direct-calls/{callId}/decline", null);

    public Task<TaskResult<DirectCall>> EndAsync(long callId) =>
        SendAsync($"api/direct-calls/{callId}/end", null);

    public Task<TaskResult<DirectCall>> LeaveAsync(long callId, string? sessionId = null) =>
        SendAsync($"api/direct-calls/{callId}/leave?sessionId={Uri.EscapeDataString(sessionId ?? string.Empty)}", null);

    public Task<TaskResult<DirectCall>> AddParticipantsAsync(long callId, IEnumerable<long> userIds) =>
        SendAsync($"api/direct-calls/{callId}/participants", new AddDirectCallParticipantsRequest
        {
            UserIds = userIds.Distinct().ToList()
        });

    private async Task<TaskResult<DirectCall>> SendAsync(string route, object? body)
    {
        var result = await _client.PrimaryNode.PostAsyncWithResponse<DirectCall>(route, body);
        if (result.Success && result.Data is not null)
            Apply(result.Data);
        return result;
    }

    private void HookHubEvents(Node node)
    {
        if (node.IsExternal)
            return;
        node.HubConnection.On<DirectCall>("Direct-Call-Update", call => Apply(call));
    }

    private void Apply(DirectCall call, bool notify = true)
    {
        if (call is null)
            return;

        var myState = call.Members.FirstOrDefault(x => x.UserId == _client.Me?.Id)?.State;
        if (call.State == DirectCallState.Ended ||
            myState is DirectCallMemberState.Declined or DirectCallMemberState.Left)
            _currentCalls.Remove(call.Id);
        else
            _currentCalls[call.Id] = call;

        if (notify)
            CallUpdated?.Invoke(call);
    }
}
