using Valour.Sdk.Client;
using Valour.Sdk.E2ee;
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

    /// <summary>
    /// The number of unread notifications, read without copying the list.
    /// </summary>
    public int UnreadCount
    {
        get
        {
            lock (_unreadLock) return _unreadNotifications.Count;
        }
    }

    public IReadOnlyDictionary<long, Notification> UnreadNotificationsLookupBySource
    {
        get
        {
            lock (_unreadLock) return new Dictionary<long, Notification>(_unreadNotificationsLookupBySource);
        }
    }

    /// <summary>
    /// Looks up the unread notification for a source (such as a message)
    /// without copying the source lookup.
    /// </summary>
    public bool TryGetUnreadBySource(long sourceId, out Notification? notification)
    {
        lock (_unreadLock) return _unreadNotificationsLookupBySource.TryGetValue(sourceId, out notification);
    }

    public NotificationService(ValourClient client) => _client = client;

    /// <summary>
    /// Fetches and decrypts the message a notification is about. The server
    /// cannot read messages, so a message notification's
    /// <see cref="Notification.Body"/> is a placeholder ("Encrypted message")
    /// and <see cref="Notification.SourceId"/> holds the message ID. Show the
    /// returned message's <see cref="Message.Content"/> in place of the body.
    /// Returns null when the notification is not about a message, or the
    /// message was deleted or cannot be read on this device.
    /// </summary>
    public async Task<Message> FetchNotificationMessageAsync(Notification notification)
    {
        if (notification?.SourceId is not { } messageId || !IsMessageSource(notification.Source))
            return null;

        Planet planet = null;
        if (notification.PlanetId is { } planetId)
        {
            planet = await _client.PlanetService.FetchPlanetAsync(planetId);
            if (planet is null)
                return null;
        }

        var message = planet is null
            ? await _client.MessageService.FetchMessageAsync(messageId)
            : await _client.MessageService.FetchMessageAsync(messageId, planet);

        return message?.DecryptionState is MessageDecryptionState.Decrypted or MessageDecryptionState.NotAttempted
            ? message
            : null;
    }

    /// <summary>
    /// Returns the one line a system notification shows for a message
    /// notification, from the decrypted message. See
    /// <see cref="NotificationPreviewText"/>. Returns null when
    /// <see cref="FetchNotificationMessageAsync"/> finds no readable message.
    /// </summary>
    public async Task<string> FetchNotificationTextAsync(Notification notification)
    {
        var message = await FetchNotificationMessageAsync(notification);
        if (message is null)
            return null;

        return NotificationPreviewText.Describe(new NotificationPreview
        {
            Content = message.Content,
            HasEmbed = message.IsEmbed(),
            AttachmentCount = message.Attachments?.Count(a => a.Type != MessageAttachmentType.Embed) ?? 0
        });
    }

    /// <summary>
    /// Whether notifications from this source are about a chat message, so
    /// <see cref="Notification.SourceId"/> is a message ID.
    /// </summary>
    public static bool IsMessageSource(NotificationSource source) => source is
        NotificationSource.DirectMessage or NotificationSource.DirectReply or NotificationSource.DirectMention or
        NotificationSource.PlanetMemberReply or NotificationSource.PlanetMemberMention or
        NotificationSource.PlanetRoleMention or NotificationSource.PlanetHereMention or
        NotificationSource.PlanetEveryoneMention or NotificationSource.ChannelActivity;

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

    // These counts run per sidebar row during rendering, so they iterate
    // under the lock instead of copying the unread list for each call.

    public int GetPlanetNotifications(long planetId)
    {
        lock (_unreadLock)
        {
            var count = 0;
            foreach (var notification in _unreadNotifications)
            {
                if (notification.PlanetId == planetId &&
                    notification.Source != NotificationSource.ChannelActivity)
                    count++;
            }
            return count;
        }
    }

    public int GetChannelNotifications(long channelId)
    {
        lock (_unreadLock)
        {
            var count = 0;
            foreach (var notification in _unreadNotifications)
            {
                if (notification.ChannelId == channelId &&
                    notification.Source != NotificationSource.ChannelActivity)
                    count++;
            }
            return count;
        }
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
