using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.JSInterop;
using Valour.Shared.Utilities;

namespace Valour.Client.Utility;

/// <summary>
/// Reports this user's presence to a small self-hosted tracker and keeps a
/// cached list of other users currently online with the modded client, so
/// UserBadges can show a "modded client" badge for them.
///
/// The online-list refresh always runs (so everyone can see who else is
/// modded), independent of whether this user has opted in to reporting
/// their own presence. A server-sent-events push tells us the moment
/// someone joins/leaves; the timer below is just a slow fallback in case
/// that connection silently drops.
/// </summary>
public static class ModdedPresenceService
{
    private const string BaseUrl = "https://skyjoshua.xyz/valour/api";
    private const string ClientKey = "f91a43d4b27a75714d4b1cf134fca32c8c50ba9a3734b945";
    private static readonly TimeSpan ReportInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ListRefreshFallbackInterval = TimeSpan.FromMinutes(5);

    private static readonly HttpClient Http = new();

    private static Timer _listTimer;
    private static Timer _reportTimer;
    private static string _reportJson;
    private static string _leaveJson;

    private static readonly HashSet<long> _onlineUserIds = new();

    /// <summary>
    /// Read-only view over the current online set. Mutated in place on refresh
    /// rather than replaced, to avoid a fresh allocation every poll.
    /// </summary>
    public static IReadOnlySet<long> OnlineUserIds => _onlineUserIds;

    /// <summary>
    /// Fired whenever the cached online list is refreshed.
    /// </summary>
    public static HybridEvent Updated;

    /// <summary>
    /// Begins listening for push updates and periodically fetching the online
    /// list as a slow fallback. Safe to call repeatedly. Does not report this
    /// user's own presence — call StartReporting for that.
    /// </summary>
    public static void StartListRefresh(IJSRuntime jsRuntime)
    {
        if (_listTimer is not null)
            return;

        _listTimer = new Timer(_ => _ = RefreshOnlineListSafeAsync(), null, TimeSpan.Zero, ListRefreshFallbackInterval);

        // Refresh immediately when the tab/app regains focus, rather than waiting
        // for the next timer tick, so badges catch up faster after switching back.
        AppLifecycle.Resumed += () => _ = RefreshOnlineListSafeAsync();

        // Push channel: the browser's EventSource handles reconnection on its
        // own, so a dropped connection just falls back to the slow poll above
        // until it reconnects.
        _ = jsRuntime.InvokeVoidAsync("moddedPresence.connect", $"{BaseUrl}/online/stream").AsTask();
    }

    /// <summary>
    /// Called from JS when the server pushes a presence-changed notification.
    /// </summary>
    [JSInvokable]
    public static void OnModdedPresenceChanged()
    {
        _ = RefreshOnlineListSafeAsync();
    }

    public static void StartReporting(long userId, string username)
    {
        // userId is sent as a string - it's a 64-bit snowflake ID that exceeds
        // JS's safe integer range, so a raw JSON number would get rounded by the
        // server's JSON.parse and silently corrupt the stored ID.
        var userIdString = userId.ToString();
        _reportJson = JsonSerializer.Serialize(new { userId = userIdString, username });
        _leaveJson = JsonSerializer.Serialize(new { userId = userIdString });

        if (_reportTimer is null)
            _reportTimer = new Timer(_ => _ = ReportPresenceSafeAsync(), null, TimeSpan.Zero, ReportInterval);

        // Report immediately and pull the refreshed list back down right after,
        // so the badge updates for us without waiting on the next poll tick.
        // (Redundant with the timer's own immediate first tick, but that's
        // harmless - just an extra idempotent report.)
        _ = ReportThenRefreshAsync();
    }

    public static void StopReporting()
    {
        _reportTimer?.Dispose();
        _reportTimer = null;

        // Actively remove ourselves rather than waiting for the tracker's
        // online window to expire, so the badge disappears for everyone right away.
        _ = LeaveThenRefreshAsync();
    }

    private static async Task ReportThenRefreshAsync()
    {
        await ReportPresenceSafeAsync();
        await RefreshOnlineListSafeAsync();
    }

    private static async Task LeaveThenRefreshAsync()
    {
        await LeavePresenceSafeAsync();
        await RefreshOnlineListSafeAsync();
    }

    private static async Task ReportPresenceSafeAsync()
    {
        try
        {
            await ReportPresenceAsync();
        }
        catch
        {
            // Best-effort — the tracker being unreachable shouldn't affect the app
        }
    }

    private static async Task LeavePresenceSafeAsync()
    {
        try
        {
            await LeavePresenceAsync();
        }
        catch
        {
            // Best-effort — the tracker being unreachable shouldn't affect the app
        }
    }

    private static async Task RefreshOnlineListSafeAsync()
    {
        try
        {
            await RefreshOnlineListAsync();
        }
        catch
        {
            // Best-effort — the tracker being unreachable shouldn't affect the app
        }
    }

    private static async Task ReportPresenceAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/presence");
        request.Headers.Add("X-Client-Key", ClientKey);
        // Payload is precomputed once in StartReporting since it never changes
        // between ticks - avoids re-serializing on every report.
        request.Content = new StringContent(_reportJson, Encoding.UTF8, "application/json");

        using var response = await Http.SendAsync(request);
    }

    private static async Task LeavePresenceAsync()
    {
        if (_leaveJson is null)
            return;

        using var request = new HttpRequestMessage(HttpMethod.Delete, $"{BaseUrl}/presence");
        request.Headers.Add("X-Client-Key", ClientKey);
        request.Content = new StringContent(_leaveJson, Encoding.UTF8, "application/json");

        using var response = await Http.SendAsync(request);
    }

    private static async Task RefreshOnlineListAsync()
    {
        var entries = await Http.GetFromJsonAsync<List<PresenceEntry>>($"{BaseUrl}/online");
        if (entries is null)
            return;

        _onlineUserIds.Clear();
        foreach (var entry in entries)
        {
            if (long.TryParse(entry.UserId, out var id))
                _onlineUserIds.Add(id);
        }

        Updated?.Invoke();
    }

    private class PresenceEntry
    {
        public string UserId { get; set; }
        public string Username { get; set; }
    }
}
