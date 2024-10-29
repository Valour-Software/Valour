using Valour.Sdk.Client;
using Valour.Shared;
using Valour.Shared.Utilities;

namespace Valour.SDK.Services;

public class NotificationService
{
    /// <summary>
    /// Run when a notification is received
    /// </summary>
    public HybridEvent<Notification> NotificationReceived;

    /// <summary>
    /// Run when notifications are cleared
    /// </summary>
    public HybridEvent NotificationsCleared;
    
    /// <summary>
    /// Pain and suffering for thee
    /// </summary>
    public IReadOnlyList<Notification> UnreadNotifications { get; private set; }
    private List<Notification> _unreadNotifications = new();
    
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

        var notifications = response.Data;

        _unreadNotifications.Clear();
        _unreadNotificationsLookupBySource.Clear();
        
        // Add to cache
        foreach (var notification in notifications)
        {
            var cached = _client.Cache.Sync(notification);
            
            // Only add if unread
            if (notification.TimeRead is not null)
                continue;
            
            _unreadNotifications.Add(cached);
            
            if (cached.SourceId is not null)
                _unreadNotificationsLookupBySource.Add(notification.SourceId!.Value, notification);
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
    
    public int GetPlanetNotifications(long planetId)
    {
        return UnreadNotifications.Count(x => x.PlanetId == planetId);
    }

    public int GetChannelNotifications(long channelId)
    {
        return UnreadNotifications.Count(x => x.ChannelId == channelId);
    }

    public void OnNotificationReceived(Notification notification)
    {
        var cached = _client.Cache.Sync(notification);   
        
        if (cached.TimeRead is null)
        {
            if (!_unreadNotifications.Contains(cached))
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