using System.Collections.Concurrent;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Valour.Config.Configs;
using Valour.Shared.Models;

namespace Valour.Server.Services;

public sealed class WebPushDeliveryClient : IDisposable
{
    public const string HttpClientName = "WebPush";
    private readonly HttpClient _httpClient;
    private readonly PushServiceClient _client;
    private readonly Lazy<VapidAuthentication> _authentication;
    private readonly object _authenticationLock = new();

    /// <summary>
    /// Primary handler for the named Web Push client. Endpoints come from
    /// clients, so connections are pinned to validated public addresses and
    /// redirects are not followed.
    /// </summary>
    public static SocketsHttpHandler CreatePrimaryHandler()
    {
        var handler = Valour.Server.Cdn.SsrfSafeConnect.CreateHandler(allowPrivate: false);
        handler.PooledConnectionLifetime = TimeSpan.FromMinutes(5);
        return handler;
    }

    public WebPushDeliveryClient(IHttpClientFactory factory)
        : this(factory.CreateClient(HttpClientName), NotificationsConfig.Current) { }

    internal WebPushDeliveryClient(HttpClient httpClient, NotificationsConfig? configuration)
    {
        _httpClient = httpClient;
        _client = new PushServiceClient(httpClient) { AutoRetryAfter = false, DefaultTimeToLive = 2419200 };
        _authentication = new Lazy<VapidAuthentication>(() => new(configuration?.PublicKey, configuration?.PrivateKey)
        {
            Subject = configuration?.Subject,
            TokenCache = new TokenCache()
        });
    }

    public Task SendAsync(ISharedPushNotificationSubscription subscription, string payload, CancellationToken token)
    {
        var target = new PushSubscription
        {
            Endpoint = subscription.Endpoint,
            Keys = new Dictionary<string, string> { ["p256dh"] = subscription.Key, ["auth"] = subscription.Auth }
        };
        // The library creates headers synchronously before its first await. Serialize that
        // preparation so concurrent sends share one cached JWT and never sign concurrently.
        // The lock is released while the returned task waits for the provider response.
        lock (_authenticationLock)
            return _client.RequestPushMessageDeliveryAsync(target, new PushMessage(payload), _authentication.Value, token);
    }

    public void Dispose()
    {
        if (_authentication.IsValueCreated) _authentication.Value.Dispose();
        _httpClient.Dispose();
    }

    private sealed class TokenCache : IVapidTokenCache
    {
        private readonly ConcurrentDictionary<string, (DateTimeOffset Expiration, string Token)> _tokens = new();
        public string? Get(string audience) => _tokens.TryGetValue(audience, out var entry) &&
            entry.Expiration > DateTimeOffset.UtcNow.AddMinutes(5) ? entry.Token : null;
        public void Put(string audience, DateTimeOffset expiration, string token) => _tokens[audience] = (expiration, token);
    }
}
