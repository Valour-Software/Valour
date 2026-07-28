using Microsoft.AspNetCore.SignalR.Client;
using Valour.Sdk.Client;
using Valour.Sdk.ModelLogic;
using Valour.Sdk.Models.Economy;
using Valour.Sdk.Nodes;
using Valour.Shared;
using Valour.Shared.Models;
using Valour.Shared.Models.Staff;
using Valour.Shared.Utilities;

namespace Valour.Sdk.Services;

public class StaffService : ServiceBase
{
    private static readonly LogOptions LogOptions = new (
        "StaffService",
        "#036bfc",
        "#fc0356",
        "#fc8403"
    );

    private readonly ValourClient _client;

    /// <summary>
    /// Fired for every live-counter push while the dashboard group is joined
    /// </summary>
    public HybridEvent<DashboardLiveStats> DashboardLiveUpdated;

    /// <summary>
    /// Fired when any user's first primary connection opens or last one closes
    /// </summary>
    public HybridEvent<DashboardPresenceEvent> DashboardPresence;

    // The primary node's HubConnection is replaced on token refresh, so the
    // registration guard tracks which connection currently has our handlers
    private HubConnection _dashboardHookedConnection;
    private bool _dashboardJoined;
    private bool _dashboardReconnectHooked;

    public StaffService(ValourClient client)
    {
        _client = client;
        SetupLogging(client.Logger, LogOptions);
    }
    
    public async Task<TaskResult> SetUserDisabledAsync(long userId, bool value, string reason)
    {
        var request = new DisableUserRequest()
        {
            UserId = userId,
            Value = value,
            Reason = reason
        };

        return await _client.PrimaryNode.PostAsync($"api/staff/disable", request);
    }

    public async Task<TaskResult> DeleteUserAsync(long userId, string reason)
    {
        var request = new DeleteUserRequest()
        {
            UserId = userId,
            Reason = reason
        };

        return await _client.PrimaryNode.PostAsync($"api/staff/delete", request);
    }

    public Task<TaskResult<StaffUserLookupResult>> LookupUserAsync(string identifier, string reason) =>
        _client.PrimaryNode.PostAsyncWithResponse<StaffUserLookupResult>("api/staff/users/lookup",
            new StaffUserLookupRequest() { Identifier = identifier, Reason = reason });

    public async Task<List<User>> GetOwnedBotsAsync(long userId)
    {
        var result = await _client.PrimaryNode.GetJsonAsync<List<User>>($"api/staff/users/{userId}/bots");
        return result.Data;
    }

    public Task<TaskResult> ResetUsernameAsync(long userId, string reason) =>
        _client.PrimaryNode.PostAsync("api/staff/users/resetname",
            new StaffResetUsernameRequest() { UserId = userId, Reason = reason });

    public Task<TaskResult> SetPriorNameHiddenAsync(long userId, bool hidden, string reason) =>
        _client.PrimaryNode.PostAsync("api/staff/users/priorname",
            new StaffSetPriorNameHiddenRequest() { UserId = userId, Hidden = hidden, Reason = reason });

    public Task<TaskResult> TriggerPasswordResetAsync(long userId, bool invalidateSessions, string reason) =>
        _client.PrimaryNode.PostAsync("api/staff/users/passwordreset",
            new StaffPasswordResetRequest() { UserId = userId, InvalidateSessions = invalidateSessions, Reason = reason });

    public Task<TaskResult> ScheduleMfaRemovalAsync(long userId, string reason) =>
        _client.PrimaryNode.PostAsync("api/staff/users/mfa/schedule",
            new StaffMfaRemovalRequest() { UserId = userId, Reason = reason });

    public Task<TaskResult> CancelMfaRemovalAsync(long userId, string reason) =>
        _client.PrimaryNode.PostAsync("api/staff/users/mfa/cancel",
            new StaffMfaRemovalRequest() { UserId = userId, Reason = reason });

    public async Task<TaskResult> VerifyUserAsync(string identifier)
    {
        var request = new VerifyUserRequest()
        {
            Identifier = identifier
        };

        return await _client.PrimaryNode.PostAsync("api/staff/users/verify", request);
    }

    public async Task<TaskResult> SendMassEmailAsync(string subject, string htmlBody)
    {
        var request = new SendMassEmailRequest()
        {
            Subject = subject,
            HtmlBody = htmlBody
        };

        return await _client.PrimaryNode.PostAsync("api/staff/email/send", request);
    }

    public async Task<Message> GetMessageAsync(long messageId)
    {
        var result = await _client.PrimaryNode.GetJsonAsync<Message>($"api/staff/messages/{messageId}");
        return result.Data;
    }

    public async Task<Report> GetReportAsync(string reportId)
    {
        var result = await _client.PrimaryNode.GetJsonAsync<Report>($"api/staff/reports/{reportId}");
        return result.Data;
    }

    public async Task<TaskResult> ResolveReportAsync(string reportId, ReportResolution resolution, string staffNotes)
    {
        var request = new ResolveReportRequest()
        {
            ReportId = reportId,
            Resolution = resolution,
            StaffNotes = staffNotes
        };

        return await _client.PrimaryNode.PostAsync("api/staff/reports/resolve", request);
    }

    public async Task<DashboardSnapshot> GetDashboardSnapshotAsync()
    {
        var result = await _client.PrimaryNode.GetJsonAsync<DashboardSnapshot>("api/staff/dashboard/snapshot");
        return result.Data;
    }

    public async Task<DashboardAnalytics> GetDashboardAnalyticsAsync(int days = 30)
    {
        var result = await _client.PrimaryNode.GetJsonAsync<DashboardAnalytics>($"api/staff/dashboard/analytics?days={days}");
        return result.Data;
    }

    /// <summary>
    /// Joins the staff dashboard realtime group on the primary node. Events
    /// arrive via <see cref="DashboardLiveUpdated"/> and
    /// <see cref="DashboardPresence"/>; the group is rejoined automatically
    /// when the node reconnects.
    /// </summary>
    public async Task<TaskResult> JoinDashboardAsync()
    {
        RegisterDashboardHandlers();
        HookDashboardReconnect();

        var result = await _client.PrimaryNode.HubConnection
            .InvokeAsync<TaskResult>("JoinStaffDashboard");

        if (result.Success)
            _dashboardJoined = true;

        return result;
    }

    public async Task LeaveDashboardAsync()
    {
        _dashboardJoined = false;
        await _client.PrimaryNode.HubConnection.InvokeAsync("LeaveStaffDashboard");
    }

    private void RegisterDashboardHandlers()
    {
        var hubConnection = _client.PrimaryNode.HubConnection;
        if (ReferenceEquals(_dashboardHookedConnection, hubConnection))
            return;

        _dashboardHookedConnection = hubConnection;

        hubConnection.On<DashboardLiveStats>(DashboardHub.LiveEvent,
            stats => DashboardLiveUpdated?.Invoke(stats));
        hubConnection.On<DashboardPresenceEvent>(DashboardHub.PresenceEvent,
            presence => DashboardPresence?.Invoke(presence));
    }

    private void HookDashboardReconnect()
    {
        if (_dashboardReconnectHooked)
            return;

        _dashboardReconnectHooked = true;
        _client.NodeService.NodeReconnected += OnNodeReconnected;
    }

    private async Task OnNodeReconnected(Node node)
    {
        if (!_dashboardJoined || !ReferenceEquals(node, _client.PrimaryNode))
            return;

        try
        {
            var result = await JoinDashboardAsync();
            if (!result.Success)
                LogError($"Failed to rejoin staff dashboard after reconnect: {result.Message}");
        }
        catch (Exception ex)
        {
            LogError("Error rejoining staff dashboard after reconnect", ex);
        }
    }
}
