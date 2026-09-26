using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Valour.Config.Configs;
using Valour.Server.Database;
using Valour.Shared.Models;
using Lib.Net.Http.WebPush;

namespace Valour.Server.Services;

public class PushNotificationService
{
    /// <summary>
    /// How long a subscription lives without being renewed. Renewal happens whenever the
    /// client app is opened (keepalive re-subscribe), so this only needs to cover long
    /// periods of inactivity. Genuinely dead endpoints are already cleaned up eagerly via
    /// WebPush 410 Gone / FCM Unregistered responses.
    /// </summary>
    public static readonly TimeSpan SubscriptionLifetime = TimeSpan.FromDays(90);

    private readonly ValourDb _db;
    private readonly ILogger<PushNotificationService> _logger;
    private readonly HostedPlanetService _hostedService;
    private readonly WebPushDeliveryClient _webPushClient;
    
    public PushNotificationService(
        ILogger<PushNotificationService> logger, 
        ValourDb db, 
        PlanetPermissionService permissionService, 
        HostedPlanetService hostedService,
        WebPushDeliveryClient webPushClient)
    {
        _logger = logger;
        _db = db;
        _hostedService = hostedService;
        
        _webPushClient = webPushClient;
    }
    
    public async Task ClearExpiredSubscriptionsAsync()
    {
        var expiredSubs = await _db.PushNotificationSubscriptions
            .Where(x => x.ExpiresAt < DateTime.UtcNow)
            .ExecuteDeleteAsync();
        
        _logger.LogInformation("Cleared {Count} expired subscriptions", expiredSubs);
    }
    
    /// <summary>
    /// Stores or renews a subscription for its user. Returns false when the
    /// subscription is invalid or its endpoint belongs to another account that
    /// the caller cannot show it shares a device subscription with.
    /// </summary>
    public async Task<bool> SubscribeAsync(PushNotificationSubscription subscription)
    {
        var validationError = PushSubscriptionPolicy.Validate(subscription);
        if (validationError is not null)
        {
            _logger.LogWarning("Rejected push subscription for user {UserId}: {Reason}", subscription?.UserId, validationError);
            return false;
        }

        var existingSubs = await _db.PushNotificationSubscriptions
            .Where(x => x.Endpoint == subscription.Endpoint)
            .ToListAsync();

        var existingUserSub = existingSubs.FirstOrDefault(x => x.UserId == subscription.UserId);
        var otherUserSubs = existingSubs.Where(x => x.UserId != subscription.UserId).ToList();

        if (otherUserSubs.Count > 0)
        {
            // Only the device that holds the subscription can move it to the
            // account now signed in there. Knowing an endpoint alone must not
            // let one user take over another user's notifications.
            if (!otherUserSubs.All(x => PushSubscriptionPolicy.IsSameDeviceSubscription(x, subscription)))
            {
                _logger.LogWarning(
                    "Rejected push subscription for user {UserId}: endpoint is registered to another account",
                    subscription.UserId);
                return false;
            }

            _db.PushNotificationSubscriptions.RemoveRange(otherUserSubs);
        }

        if (existingUserSub is not null)
        {
            // Update existing subscription
            existingUserSub.DeviceType = subscription.DeviceType;
            existingUserSub.Auth = subscription.Auth;
            existingUserSub.Key = subscription.Key;
            existingUserSub.AuthTokenId = subscription.AuthTokenId;
            existingUserSub.ExpiresAt = DateTime.UtcNow.Add(SubscriptionLifetime);
        }
        else
        {
            var newUserSub = subscription.ToDatabase();
            newUserSub.Id = IdManager.Generate();
            // Set explicitly rather than relying on the database default (which is only 7 days)
            newUserSub.ExpiresAt = DateTime.UtcNow.Add(SubscriptionLifetime);

            // Add new subscription
            await _db.PushNotificationSubscriptions.AddAsync(newUserSub);
        }

        await _db.SaveChangesAsync();

        // Keep the most recently renewed subscriptions and drop the rest.
        var overflowIds = await _db.PushNotificationSubscriptions
            .AsNoTracking()
            .Where(x => x.UserId == subscription.UserId)
            .OrderByDescending(x => x.ExpiresAt)
            .ThenByDescending(x => x.Id)
            .Skip(PushSubscriptionPolicy.MaxSubscriptionsPerUser)
            .Select(x => x.Id)
            .ToListAsync();

        if (overflowIds.Count > 0)
        {
            await _db.PushNotificationSubscriptions
                .Where(x => overflowIds.Contains(x.Id))
                .ExecuteDeleteAsync();
        }

        return true;
    }

    public async Task<bool> IsSubscribedAsync(long userId, string endpoint, NotificationDeviceType deviceType)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            return false;

        return await _db.PushNotificationSubscriptions
            .AsNoTracking()
            .AnyAsync(x =>
                x.UserId == userId &&
                x.Endpoint == endpoint &&
                x.DeviceType == deviceType &&
                x.ExpiresAt > DateTime.UtcNow);
    }
    
    /// <summary>
    /// Removes a user's own registration for an endpoint. Registrations held
    /// by other accounts are left alone.
    /// </summary>
    public async Task UnsubscribeAsync(ISharedPushNotificationSubscription subscription)
    {
        var deleted = await _db.PushNotificationSubscriptions
            .Where(x => x.Endpoint == subscription.Endpoint && x.UserId == subscription.UserId)
            .ExecuteDeleteAsync();

        _logger.LogInformation("Deleted {Count} subscriptions for user {UserId}", deleted, subscription.UserId);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async Task UnsubscribeAsync(string endpoint)
    {
        // Get all subscriptions for this endpoint
        var subscriptions = await _db.PushNotificationSubscriptions
            .Where(x => x.Endpoint == endpoint)
            .ExecuteDeleteAsync();
        
        _logger.LogInformation("Deleted {Count} subscriptions for endpoint {Endpoint}", subscriptions, endpoint);
    }
    
    /// <summary>
    /// The largest payload sent, in UTF-8 bytes. Web Push and FCM both accept
    /// about 4 KB, and Web Push encryption adds about 100 bytes. A message
    /// envelope that would pass this is left out, and the device shows the
    /// placeholder body instead of the text.
    /// </summary>
    internal const int MaxPayloadBytes = 3584;

    private sealed class PushPayload
    {
        [JsonPropertyName("title")] public string Title { get; init; }
        [JsonPropertyName("message")] public string Message { get; init; }
        [JsonPropertyName("iconUrl")] public string IconUrl { get; init; }
        [JsonPropertyName("url")] public string Url { get; init; }
        [JsonPropertyName("notificationId")] public string NotificationId { get; init; }
        [JsonPropertyName("sourceId")] public string SourceId { get; init; }
        [JsonPropertyName("timeSent")] public long TimeSent { get; init; }

        [JsonPropertyName("planetId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string PlanetId { get; init; }

        [JsonPropertyName("channelId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string ChannelId { get; init; }

        /// <summary>The message envelope, base64. Only the recipient's device can decrypt it.</summary>
        [JsonPropertyName("envelope"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Envelope { get; set; }
    }

    // Base64 and non-ASCII text are written as they are, not as \u escapes,
    // so more envelopes fit. Readers parse the JSON; it is never put in HTML.
    private static readonly JsonSerializerOptions PayloadJsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    internal static string GetPayload(NotificationContent content)
    {
        var timeSent = content.TimeSent == default ? DateTime.UtcNow : content.TimeSent;

        var payload = new PushPayload
        {
            Title = content.Title,
            Message = content.Message,
            IconUrl = content.IconUrl,
            Url = content.Url,
            NotificationId = content.NotificationId?.ToString(),
            SourceId = content.SourceId?.ToString(CultureInfo.InvariantCulture),
            TimeSent = new DateTimeOffset(DateTime.SpecifyKind(timeSent, DateTimeKind.Utc))
                .ToUnixTimeMilliseconds(),
            PlanetId = content.PlanetId?.ToString(CultureInfo.InvariantCulture),
            ChannelId = content.ChannelId?.ToString(CultureInfo.InvariantCulture),
        };

        var json = JsonSerializer.Serialize(payload, PayloadJsonOptions);
        if (content.Envelope is not { Length: > 0 })
            return json;

        payload.Envelope = Convert.ToBase64String(content.Envelope);
        var withEnvelope = JsonSerializer.Serialize(payload, PayloadJsonOptions);
        return Encoding.UTF8.GetByteCount(withEnvelope) <= MaxPayloadBytes ? withEnvelope : json;
    }
    
    internal static string GetNotificationImageUrl(string iconUrl, string appBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(iconUrl))
            return null;

        if (!Uri.TryCreate(appBaseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri) ||
            !Uri.TryCreate(baseUri, iconUrl, out var imageUri))
            return null;

        return imageUri.Scheme is "https" or "http" ? imageUri.AbsoluteUri : null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Task SendNotificationAsync(
        ISharedPushNotificationSubscription sub,
        string payload,
        ConcurrentBag<string> unsubscribes,
        CancellationToken cancellationToken
    )
    {
        return sub.DeviceType switch
        {
            NotificationDeviceType.AndroidFcm or NotificationDeviceType.AndroidFcmData =>
                SendFcmNotificationAsync(sub, payload, unsubscribes),
            _ => SendWebPushNotificationAsync(sub, payload, unsubscribes, cancellationToken),
        };
    }

    private async Task SendWebPushNotificationAsync(
        ISharedPushNotificationSubscription sub,
        string payload,
        ConcurrentBag<string> unsubscribes,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await _webPushClient.SendAsync(sub, payload, cancellationToken);

            _logger.LogDebug("Sent notification to {Endpoint}", sub.Endpoint);
        }
        catch (PushServiceClientException ex)
        {
            var provider = new Uri(sub.Endpoint).Host;
            switch (ClassifyWebPushFailure(ex.StatusCode, ex.Body))
            {
                case WebPushFailure.SubscriptionGone:
                    _logger.LogInformation("Subscription {Endpoint} is no longer valid", sub.Endpoint);
                    unsubscribes.Add(sub.Endpoint);
                    break;
                case WebPushFailure.SubscriptionRejected:
                    // The provider refuses this subscription for good. Providers return
                    // 403 when a subscription was created with a different application
                    // server key (Apple reports it as BadJwtToken), so it can never be
                    // delivered to. The device subscribes again the next time the app
                    // opens. If every subscription of one provider lands here, check
                    // the VAPID settings instead.
                    _logger.LogWarning(
                        "Removing push subscription rejected by {Provider} ({StatusCode}): {Reason}",
                        provider, (int)ex.StatusCode, ex.Body);
                    unsubscribes.Add(sub.Endpoint);
                    break;
                case WebPushFailure.Transient:
                    _logger.LogWarning("Web Push provider {Provider} failed ({StatusCode}): {Reason}",
                        provider, (int)ex.StatusCode, ex.Body);
                    break;
                default:
                    _logger.LogError(ex, "Failed to send notification to {Provider} ({StatusCode}): {Reason}",
                        provider, (int)ex.StatusCode, ex.Body);
                    break;
            }
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Web Push delivery timed out for provider {Provider}", new Uri(sub.Endpoint).Host);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Web Push transport failed for provider {Provider}", new Uri(sub.Endpoint).Host);
        }
    }

    public enum WebPushFailure
    {
        /// <summary>The subscription expired or was removed by the browser.</summary>
        SubscriptionGone,
        /// <summary>The provider permanently refuses messages for this subscription.</summary>
        SubscriptionRejected,
        /// <summary>The provider had a temporary problem; later messages may succeed.</summary>
        Transient,
        /// <summary>The request itself was wrong, which points to a server bug.</summary>
        Unexpected
    }

    /// <summary>
    /// Sorts a Web Push provider error response. 404 and 410 mean the
    /// subscription no longer exists. 403 means the provider rejects the
    /// application server's VAPID credentials for this subscription. FCM
    /// answers some broken subscriptions with a 500 that it marks as permanent
    /// ("do not retry"); those are dropped as well.
    /// </summary>
    internal static WebPushFailure ClassifyWebPushFailure(HttpStatusCode status, string? body)
    {
        switch (status)
        {
            case HttpStatusCode.NotFound:
            case HttpStatusCode.Gone:
                return WebPushFailure.SubscriptionGone;
            case HttpStatusCode.Forbidden:
                return WebPushFailure.SubscriptionRejected;
            case HttpStatusCode.RequestTimeout:
            case HttpStatusCode.TooManyRequests:
                return WebPushFailure.Transient;
        }

        if ((int)status >= 500)
        {
            return body?.Contains("do not retry", StringComparison.OrdinalIgnoreCase) == true
                ? WebPushFailure.SubscriptionRejected
                : WebPushFailure.Transient;
        }

        return WebPushFailure.Unexpected;
    }

    private async Task SendFcmNotificationAsync(
        ISharedPushNotificationSubscription sub,
        string payload,
        ConcurrentBag<string> unsubscribes
    )
    {
        if (FirebaseAdmin.Messaging.FirebaseMessaging.DefaultInstance is null)
        {
            _logger.LogWarning("Firebase not initialized, skipping FCM notification to {Endpoint}", sub.Endpoint);
            return;
        }

        var message = BuildFcmMessage(sub.Endpoint, sub.DeviceType, payload,
            HostingConfig.Current?.AppBaseUrl ?? "https://app.valour.gg");

        try
        {
            await FirebaseAdmin.Messaging.FirebaseMessaging.DefaultInstance.SendAsync(message);
            _logger.LogDebug("Sent FCM notification to {Endpoint}", sub.Endpoint);
        }
        catch (FirebaseAdmin.Messaging.FirebaseMessagingException ex) when (ex.MessagingErrorCode is
                   FirebaseAdmin.Messaging.MessagingErrorCode.Unregistered or
                   FirebaseAdmin.Messaging.MessagingErrorCode.SenderIdMismatch)
        {
            // Unregistered tokens were removed from the device. A sender id
            // mismatch means the token belongs to another Firebase project.
            _logger.LogInformation("FCM token {Endpoint} is no longer valid ({Error})", sub.Endpoint, ex.MessagingErrorCode);
            unsubscribes.Add(sub.Endpoint);
        }
        catch (FirebaseAdmin.Messaging.FirebaseMessagingException ex) when (ex.MessagingErrorCode is
                   FirebaseAdmin.Messaging.MessagingErrorCode.Unavailable or
                   FirebaseAdmin.Messaging.MessagingErrorCode.Internal or
                   FirebaseAdmin.Messaging.MessagingErrorCode.QuotaExceeded)
        {
            _logger.LogWarning("FCM failed temporarily ({Error}): {Message}", ex.MessagingErrorCode, ex.Message);
        }
        catch (FirebaseAdmin.Messaging.FirebaseMessagingException ex)
        {
            _logger.LogError(ex, "Failed to send FCM notification to {Endpoint}", sub.Endpoint);
        }
    }
    
    /// <summary>
    /// Builds the FCM message for a payload from <see cref="GetPayload"/>.
    /// Apps registered as <see cref="NotificationDeviceType.AndroidFcmData"/>
    /// receive a data message with every payload field and display it
    /// themselves, after decrypting the message text when they can. Other
    /// apps receive a notification message, which Android displays as sent.
    /// </summary>
    internal static FirebaseAdmin.Messaging.Message BuildFcmMessage(string token, NotificationDeviceType deviceType,
        string payload, string appBaseUrl)
    {
        var content = JsonSerializer.Deserialize<JsonElement>(payload);
        string Field(string name) =>
            content.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        var title = Field("title") ?? "";
        var body = Field("message") ?? "";
        var imageUrl = GetNotificationImageUrl(Field("iconUrl"), appBaseUrl);
        var url = Field("url");

        var timeSentMs = content.TryGetProperty("timeSent", out var timeSentProp)
                         && timeSentProp.TryGetInt64(out var parsedTimeSent)
            ? parsedTimeSent
            : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        if (deviceType == NotificationDeviceType.AndroidFcmData)
        {
            var data = new Dictionary<string, string>
            {
                ["title"] = title,
                ["message"] = body,
                ["url"] = url ?? "",
                ["timeSent"] = timeSentMs.ToString(CultureInfo.InvariantCulture),
            };

            void AddIfSet(string key, string value)
            {
                if (!string.IsNullOrEmpty(value))
                    data[key] = value;
            }

            AddIfSet("iconUrl", imageUrl);
            AddIfSet("notificationId", Field("notificationId"));
            AddIfSet("sourceId", Field("sourceId"));
            AddIfSet("planetId", Field("planetId"));
            AddIfSet("channelId", Field("channelId"));
            AddIfSet("envelope", Field("envelope"));

            return new FirebaseAdmin.Messaging.Message
            {
                Token = token,
                Data = data,
                // Data messages are delivered right away only at high
                // priority. Each one is shown to the person, which Android
                // requires to keep delivering them that way.
                Android = new FirebaseAdmin.Messaging.AndroidConfig
                {
                    Priority = FirebaseAdmin.Messaging.Priority.High,
                },
            };
        }

        return new FirebaseAdmin.Messaging.Message
        {
            Token = token,
            Notification = new FirebaseAdmin.Messaging.Notification
            {
                Title = title,
                Body = body,
                ImageUrl = imageUrl,
            },
            Android = new FirebaseAdmin.Messaging.AndroidConfig
            {
                Notification = new FirebaseAdmin.Messaging.AndroidNotification
                {
                    ChannelId = "valour_default",
                    // Without an explicit event time Android cards render a bogus date
                    EventTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(timeSentMs).UtcDateTime,
                },
            },
            Data = new Dictionary<string, string> { ["url"] = url ?? "" },
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal async Task SendParallelNotificationsAsync(
        Valour.Database.PushNotificationSubscription[] subs, 
        string payload
    )
    {
        var unsubscribes = new ConcurrentBag<string>();

        // Rows stored before endpoint validation existed may point anywhere,
        // so every endpoint is checked again before the server contacts it.
        var deliverable = subs.Where(x => PushSubscriptionPolicy.Validate(x) is null).ToArray();
        if (deliverable.Length != subs.Length)
        {
            _logger.LogWarning("Skipped {Count} push subscriptions with unsupported endpoints",
                subs.Length - deliverable.Length);
        }

        await Parallel.ForEachAsync(deliverable, async (sub, cancellationToken) =>
        {
            await SendNotificationAsync(sub, payload, unsubscribes, cancellationToken);
        });
        
        // Remove invalid subscriptions
        foreach (var endpoint in unsubscribes)
        {
            await UnsubscribeAsync(endpoint);
        }
    }
    
    /// <summary>
    /// Sends a notification to the given user
    /// </summary>
    public async Task SendUserPushNotificationAsync(long userId, NotificationContent content)
    {
        // Get user's push subscriptions
        var subs = await _db.PushNotificationSubscriptions
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .ToArrayAsync();
        
        var payload = GetPayload(content);
        
        await SendParallelNotificationsAsync(subs, payload);
    }

    /// <summary>
    /// Sends a notification to the given users.
    /// </summary>
    public async Task SendUsersPushNotificationAsync(long[] userIds, NotificationContent content)
    {
        if (userIds is null || userIds.Length == 0)
            return;

        var dedupedUserIds = userIds.Distinct().ToArray();
        if (dedupedUserIds.Length == 0)
            return;

        const int userChunkSize = 2_000;
        var allSubs = new List<Valour.Database.PushNotificationSubscription>();
        foreach (var userBatch in dedupedUserIds.Chunk(userChunkSize))
        {
            var subs = await _db.PushNotificationSubscriptions
                .AsNoTracking()
                .Where(x => userBatch.Contains(x.UserId))
                .ToArrayAsync();

            if (subs.Length > 0)
                allSubs.AddRange(subs);
        }

        if (allSubs.Count == 0)
            return;

        var payload = GetPayload(content);
        await SendParallelNotificationsAsync(allSubs.ToArray(), payload);
    }
    
    /// <summary>
    /// Sends a notification to the given member
    /// </summary>
    public async Task SendMemberPushNotificationAsync(long memberId, NotificationContent content)
    {
        // Get subscriptions
        var subscriptions = await _db.PlanetMembers
            .AsNoTracking()
            .Where(x => x.Id == memberId)
            .Select(x => x.User.NotificationSubscriptions)
            // Flatten
            .SelectMany(x => x)
            .ToArrayAsync();
        
        var payload = GetPayload(content);
        
        await SendParallelNotificationsAsync(subscriptions, payload);
    }
    
    /// <summary>
    /// Sends a notification to all users in a role
    /// </summary>
    public async Task SendRolePushNotificationsAsync(long roleId, NotificationContent content)
    {
        var role = await _db.PlanetRoles
            .AsNoTracking()
            .Select(x => new
            {
                Id = x.Id,
                PlanetId = x.PlanetId,
                FlagBitIndex = x.FlagBitIndex
            })
            .FirstOrDefaultAsync(x => x.Id == roleId);

        if (role is null)
        {
            _logger.LogWarning("Role {RoleId} not found for push notification", roleId);
            return;
        }

        var subscriptions = await _db.PlanetMembers
            .AsNoTracking()
            .WithRoleByLocalIndex(role.PlanetId, role.FlagBitIndex)
            .Select(x => x.User.NotificationSubscriptions)
            // Flatten
            .SelectMany(x => x)
            .ToArrayAsync();

        var payload = GetPayload(content);

        await SendParallelNotificationsAsync(subscriptions, payload);

        _logger.LogInformation("Sent role mention for {SubscriptionCount} subscriptions", subscriptions.Length);
    }
}
