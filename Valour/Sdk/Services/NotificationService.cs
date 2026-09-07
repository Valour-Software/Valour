using Valour.Sdk.Client;
using Valour.Shared;
using Valour.Shared.Models;
using Valour.Shared.Utilities;

namespace Valour.Sdk.Services;

public class NotificationService
{
    /// <summary>
    /// Run when a notification is received
    /// </summary>
    public HybridEvent<Notification> NotificationReceived;

    /// <summary>
    /// Run when an existing unread notification's content is updated in place
    /// (coalesced channel activity). Presentation surfaces (sound, popups)
    /// intentionally do NOT fire for these — only content renderers should.
    /// </summary>
    public HybridEvent<Notification> NotificationContentUpdated;

    /// <summary>
    /// Run when notifications are cleared
    /// </summary>
    public HybridEvent NotificationsCleared;
    
    private readonly object _unreadLock = new();
    private readonly List<Notification> _unreadNotifications = new();
    private readonly Dictionary<long, Notification> _unreadNotificationsLookupBySource = new();
    private readonly ValourClient _client;

    public IReadOnlyList<Notification> UnreadNotifications => GetUnreadInternal();

    public List<Notification> GetUnreadInternal()
    {
        lock (_unreadLock) return _unreadNotifications.ToList();
    }

    public IReadOnlyDictionary<long, Notification> UnreadNotificationsLookupBySource
    {
        get
        {
            lock (_unreadLock) return new Dictionary<long, Notification>(_unreadNotificationsLookupBySource);
        }
    }

    public NotificationService(ValourClient client) => _client = client;

    public async Task LoadUnreadNotificationsAsync()
    {
        var response = await _client.PrimaryNode.GetJsonAsync<List<Notification>>($"api/notifications/self/unread/all");

        if (!response.Success)
            return;

        ApplyUnreadNotifications(response.Data);
    }

    public void ApplyUnreadNotifications(IEnumerable<Notification> notifications)
    {
        var unread = (notifications ?? []).Where(x => x is not null)
            .Select(x => x.Sync(_client)).Where(x => x.TimeRead is null)
            .DistinctBy(x => x.Id).ToList();
        lock (_unreadLock)
        {
            _unreadNotifications.Clear();
            _unreadNotificationsLookupBySource.Clear();
            _unreadNotifications.AddRange(unread);
            foreach (var notification in unread)
                if (notification.SourceId is { } sourceId)
                    _unreadNotificationsLookupBySource[sourceId] = notification;
        }
    }

    public async Task<TaskResult> MarkNotificationRead(Notification notification, bool value)
    {
        var result = await _client.PrimaryNode.PostAsync($"api/notifications/self/{notification.Id}/read/{value}", null);
        return result;
    }

    public async Task<TaskResult> ClearNotificationsAsync()
    {
        var result = await _client.PrimaryNode.PostAsync("api/notifications/self/clear", null);
        return result;
    }
    
    // Channel activity notifications are excluded from badge counts on
    // purpose: the red badge is reserved for direct relevance (mentions,
    // replies). Activity entries live in the inbox and the unread dot only.

    public int GetPlanetNotifications(long planetId)
    {
        return UnreadNotifications.Count(x => x.PlanetId == planetId
                                              && x.Source != NotificationSource.ChannelActivity);
    }

    public int GetChannelNotifications(long channelId)
    {
        return UnreadNotifications.Count(x => x.ChannelId == channelId
                                              && x.Source != NotificationSource.ChannelActivity);
    }

    public void OnNotificationReceived(Notification notification)
    {
        if (notification is null) return;
        var cached = notification.Sync(_client);
        bool contentUpdated;
        lock (_unreadLock)
        {
            var index = _unreadNotifications.FindIndex(x => x.Id == cached.Id);
            contentUpdated = index >= 0 && cached.TimeRead is null;
            // Sync may already have changed the canonical model's source ID.
            foreach (var oldSourceId in _unreadNotificationsLookupBySource
                         .Where(x => x.Value.Id == cached.Id).Select(x => x.Key).ToList())
                _unreadNotificationsLookupBySource.Remove(oldSourceId);

            if (cached.TimeRead is null)
            {
                if (index >= 0) _unreadNotifications[index] = cached;
                else _unreadNotifications.Add(cached);
                if (cached.SourceId is { } sourceId)
                    _unreadNotificationsLookupBySource[sourceId] = cached;
            }
            else if (index >= 0)
                _unreadNotifications.RemoveAt(index);
        }

        if (contentUpdated) NotificationContentUpdated?.Invoke(cached);
        else NotificationReceived?.Invoke(cached);
    }

    /// <summary>
    /// Triggered by the server when notifications are cleared,
    /// so that cross-device notifications are synced
    /// </summary>
    public void OnNotificationsCleared()
    {
        lock (_unreadLock)
        {
            _unreadNotifications.Clear();
            _unreadNotificationsLookupBySource.Clear();
        }
        NotificationsCleared?.Invoke();
    }
}
