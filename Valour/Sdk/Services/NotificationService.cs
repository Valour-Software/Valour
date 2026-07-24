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
    
    /// <summary>
    /// Pain and suffering for thee
    /// </summary>
    public IReadOnlyList<Notification> UnreadNotifications { get; private set; }
    private List<Notification> _unreadNotifications = new();
    
    // Needs to be here to be used as virtualized, unfortunately
    public List<Notification> GetUnreadInternal() => _unreadNotifications;
    
    /// <summary>
    /// A set from the source of notifications to the notification.
    /// Used for extremely efficient lookups.
    /// </summary>
    public IReadOnlyDictionary<long, Notification> UnreadNotificationsLookupBySource { get; private set; }
    private Dictionary<long, Notification> _unreadNotificationsLookupBySource = new();
    
    private readonly ValourClient _client;
    
    public NotificationService(ValourClient client)
    {
        _client = client;
        
        UnreadNotifications = _unreadNotifications;
        UnreadNotificationsLookupBySource = _unreadNotificationsLookupBySource;
    }
    
    public async Task LoadUnreadNotificationsAsync()
    {
        var response = await _client.PrimaryNode.GetJsonAsync<List<Notification>>($"api/notifications/self/unread/all");

        if (!response.Success)
            return;

        ApplyUnreadNotifications(response.Data);
    }

    public void ApplyUnreadNotifications(IEnumerable<Notification> notifications)
    {
        notifications ??= [];

        _unreadNotifications.Clear();
        _unreadNotificationsLookupBySource.Clear();
        
        // Add to cache
        foreach (var notification in notifications)
        {
            var cached = notification.Sync(_client);
            
            // Only add if unread
            if (notification.TimeRead is not null)
                continue;
            
            _unreadNotifications.Add(cached);

            if (cached.SourceId is not null)
            {
                _unreadNotificationsLookupBySource[notification.SourceId!.Value] = notification;
            }
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
        var cached = notification.Sync(_client);
        
        if (cached.TimeRead is null)
        {
            // Coalesced notifications (channel activity) re-relay the same id
            // with updated content. Notifications have no model cache, so
            // replace the list entry in place (keeping its position). Updates
            // fire NotificationContentUpdated only — re-firing
            // NotificationReceived would replay sounds and popups per update
            var existingIndex = _unreadNotifications.FindIndex(x => x.Id == cached.Id);
            if (existingIndex >= 0)
            {
                _unreadNotifications[existingIndex] = cached;

                if (cached.SourceId is not null)
                {
                    _unreadNotificationsLookupBySource[cached.SourceId.Value] = cached;
                }

                NotificationContentUpdated?.Invoke(cached);
                return;
            }

            _unreadNotifications.Add(cached);

            if (cached.SourceId is not null)
            {
                _unreadNotificationsLookupBySource[cached.SourceId.Value] = cached;
            }
        }
        else
        {
            _unreadNotifications.RemoveAll(x => x.Id == cached.Id);
            if (cached.SourceId is not null)
            {
                _unreadNotificationsLookupBySource.Remove(cached.SourceId.Value);
            }
        }
        
        NotificationReceived?.Invoke(cached);
    }

    /// <summary>
    /// Triggered by the server when notifications are cleared,
    /// so that cross-device notifications are synced
    /// </summary>
    public void OnNotificationsCleared()
    {
        _unreadNotifications.Clear();
        _unreadNotificationsLookupBySource.Clear();
        
        NotificationsCleared?.Invoke();
    }
}
