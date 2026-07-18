using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Valour.Config.Configs;
using Valour.Server.Cdn;
using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Server.Services;

/// <summary>
/// Connection + signing material for one LiveKit deployment. The instance-wide
/// SFU (from <see cref="VoiceConfig"/>) and every bring-your-own-voice planet
/// each get one of these; all LiveKit operations are parameterized by it.
/// </summary>
/// <param name="Url">Client-facing websocket URL (wss://...).</param>
/// <param name="ApiUrl">Server-facing HTTP base for RoomService; null derives it from <paramref name="Url"/>.</param>
/// <param name="ApiKey">Token issuer id.</param>
/// <param name="ApiSecret">HS256 signing secret.</param>
/// <param name="External">True for owner-supplied deployments (BYO voice) — Twirp calls
/// then go through the SSRF-safe connect path so a malicious URL can't reach private ranges.</param>
public readonly record struct LiveKitCredentials(
    string Url, string ApiUrl, string ApiKey, string ApiSecret, bool External)
{
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(Url) &&
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(ApiSecret);

    /// <summary>
    /// Server-facing HTTP base for the RoomService: the explicit API url when set,
    /// else derived from the websocket url (wss→https, ws→http).
    /// </summary>
    public string ApiBase()
    {
        if (!string.IsNullOrWhiteSpace(ApiUrl))
            return ApiUrl.TrimEnd('/');

        var url = Url.Trim();
        if (url.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
            url = "https://" + url["wss://".Length..];
        else if (url.StartsWith("ws://", StringComparison.OrdinalIgnoreCase))
            url = "http://" + url["ws://".Length..];

        return url.TrimEnd('/');
    }
}

/// <summary>
/// Self-hostable voice/video backend built on LiveKit SFUs. Unlike RealtimeKit
/// there is no managed control plane and no per-call HTTP round trip to mint a
/// token: LiveKit access tokens are locally-signed HS256 JWTs, and rooms are
/// named deterministically from the channel id (auto-created on first join,
/// auto-closed on empty_timeout). Room administration (list/remove/close) uses
/// LiveKit's Twirp RoomService over HTTP with a short-lived admin token.
///
/// Every operation is credential-scoped so the same driver serves both the
/// instance-wide SFU (this class's <see cref="IVoiceProvider"/> surface) and
/// per-planet bring-your-own-voice SFUs (via the *WithCredentials methods,
/// orchestrated by <see cref="PlanetVoiceService"/>). Media stays entirely on
/// the SFU operator's infrastructure — the point of the self-hosted driver.
/// </summary>
public class LiveKitService : IVoiceProvider
{
    private const string RoomPrefix = "valour-";
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(6);
    private static readonly TimeSpan AdminTokenLifetime = TimeSpan.FromMinutes(5);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<LiveKitService> _logger;

    // channelId -> room name for the INSTANCE SFU. LiveKit is otherwise stateless;
    // this is a hot cache reconstructable at any time from ListRooms.
    private readonly ConcurrentDictionary<long, string> _roomsByChannel = new();

    // Lazily-built SSRF-safe client for external (owner-supplied) endpoints.
    private HttpClient _externalHttpClient;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public LiveKitService(IHttpClientFactory httpClientFactory, ILogger<LiveKitService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public VoiceProvider Kind => VoiceProvider.LiveKit;

    public bool IsConfigured => VoiceConfig.Current?.LiveKitConfigured == true;

    /// <summary>Credentials for the instance-wide SFU from VoiceConfig.</summary>
    public static LiveKitCredentials InstanceCredentials
    {
        get
        {
            var config = VoiceConfig.Current;
            return new LiveKitCredentials(
                config?.LiveKitUrl ?? string.Empty,
                config?.LiveKitApiUrl,
                config?.LiveKitApiKey ?? string.Empty,
                config?.LiveKitApiSecret ?? string.Empty,
                External: false);
        }
    }

    // ================= IVoiceProvider (instance SFU) =================

    public Task<TaskResult<RealtimeKitVoiceTokenResponse>> CreateParticipantTokenAsync(
        Channel channel, long userId, string displayName, string? sessionId)
    {
        if (!IsConfigured)
        {
            return Task.FromResult(TaskResult<RealtimeKitVoiceTokenResponse>.FromFailure(
                "LiveKit is not configured on the server."));
        }

        var response = CreateParticipantTokenWithCredentials(
            InstanceCredentials, channel.Id, userId, displayName, sessionId);

        _roomsByChannel[channel.Id] = response.MeetingId;

        return Task.FromResult(TaskResult<RealtimeKitVoiceTokenResponse>.FromData(response));
    }

    public Task KickUserFromTrackedChannelAsync(long channelId, long userId) =>
        KickUserWithCredentialsAsync(InstanceCredentials, channelId, userId);

    public Task KickUserSessionFromTrackedChannelAsync(long channelId, long userId, string? sessionId) =>
        KickUserSessionWithCredentialsAsync(InstanceCredentials, channelId, userId, sessionId);

    public async Task CloseTrackedMeetingAsync(long channelId, string reason)
    {
        await CloseMeetingWithCredentialsAsync(InstanceCredentials, RoomName(channelId), reason, channelId);
        _roomsByChannel.TryRemove(channelId, out _);
    }

    public Task<TaskResult> CloseMeetingAsync(string meetingId, string reason, long? channelId = null) =>
        CloseMeetingWithCredentialsAsync(InstanceCredentials, meetingId, reason, channelId);

    public Task<HashSet<long>?> GetConnectedUserIdsAsync(long channelId, string meetingId) =>
        GetConnectedUserIdsWithCredentialsAsync(InstanceCredentials, meetingId);

    public IReadOnlyDictionary<long, string> GetTrackedChannelMeetingIds() =>
        new Dictionary<long, string>(_roomsByChannel);

    public void RemoveMeetingMapping(long channelId) => _roomsByChannel.TryRemove(channelId, out _);

    /// <summary>
    /// Reconstructs the channel → room map from the instance SFU's live rooms.
    /// LiveKit keeps no external meeting table; the room list is the source of truth.
    /// </summary>
    public async Task<Dictionary<long, string>> LoadOpenMeetingMappingsAsync()
    {
        var result = new Dictionary<long, string>();
        if (!IsConfigured)
            return result;

        var rooms = await ListRoomsAsync(InstanceCredentials);
        if (rooms is null)
            return result;

        foreach (var room in rooms)
        {
            var channelId = ChannelIdFromRoom(room);
            if (channelId is null)
                continue;

            result[channelId.Value] = room;
            _roomsByChannel[channelId.Value] = room;
        }

        return result;
    }

    /// <summary>
    /// No-op: LiveKit rooms self-close when empty (empty_timeout), so there are no
    /// lingering server-side sessions to sweep the way RealtimeKit needs.
    /// </summary>
    public Task CloseOrphanedSessionsAsync(int minParticipants) => Task.CompletedTask;

    // ================= Credential-scoped core (instance + BYO planets) =================

    /// <summary>
    /// Mints a join token + response for any LiveKit deployment. Pure signing —
    /// no network call, so it cannot fail against a live SFU.
    /// </summary>
    public RealtimeKitVoiceTokenResponse CreateParticipantTokenWithCredentials(
        LiveKitCredentials creds, long channelId, long userId, string displayName, string? sessionId)
    {
        var roomName = RoomName(channelId);
        var identity = BuildIdentity(userId, sessionId);
        var metadata = BuildParticipantMetadata(channelId, userId, sessionId);

        var grant = new Dictionary<string, object>
        {
            ["roomJoin"] = true,
            ["room"] = roomName,
            ["canPublish"] = true,
            ["canSubscribe"] = true,
            ["canPublishData"] = true,
        };

        var token = SignAccessToken(creds.ApiKey, creds.ApiSecret, identity, displayName,
            metadata, grant, TokenLifetime);

        return new RealtimeKitVoiceTokenResponse
        {
            Provider = VoiceProviderExtensions.LiveKitWire,
            MeetingId = roomName,
            ParticipantId = identity,
            AuthToken = token,
            Url = creds.Url,
            SelfHosted = creds.External,
        };
    }

    public async Task KickUserWithCredentialsAsync(LiveKitCredentials creds, long channelId, long userId)
    {
        var room = RoomName(channelId);
        var identities = await GetParticipantIdentitiesAsync(creds, room);
        if (identities is null)
            return;

        foreach (var identity in identities)
        {
            if (ExtractUserId(identity) == userId)
                await RemoveParticipantAsync(creds, room, identity);
        }
    }

    public async Task KickUserSessionWithCredentialsAsync(
        LiveKitCredentials creds, long channelId, long userId, string? sessionId)
    {
        var normalized = NormalizeSessionId(sessionId);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            await KickUserWithCredentialsAsync(creds, channelId, userId);
            return;
        }

        await RemoveParticipantAsync(creds, RoomName(channelId), BuildIdentity(userId, normalized));
    }

    public async Task<TaskResult> CloseMeetingWithCredentialsAsync(
        LiveKitCredentials creds, string room, string reason, long? channelId = null)
    {
        if (!creds.IsValid)
            return TaskResult.FromFailure("LiveKit is not configured.");

        if (string.IsNullOrWhiteSpace(room))
            return TaskResult.FromFailure("Room name is required.");

        // A room that no longer exists is the desired end state — treat 404 as success
        // so best-effort teardown of an already-gone/auto-expired room isn't a "failure".
        var ok = await TwirpCommandAsync(creds, "DeleteRoom", new { room }, "delete room", notFoundIsOk: true);
        if (ok)
        {
            _logger.LogInformation("Closed LiveKit room {Room} for channel {ChannelId}. Reason: {Reason}",
                room, channelId, reason);
            return TaskResult.SuccessResult;
        }

        return TaskResult.FromFailure("LiveKit room deletion failed.");
    }

    public async Task<HashSet<long>?> GetConnectedUserIdsWithCredentialsAsync(LiveKitCredentials creds, string room)
    {
        var identities = await GetParticipantIdentitiesAsync(creds, room);
        if (identities is null)
            return null;

        var userIds = new HashSet<long>();
        foreach (var identity in identities)
        {
            var userId = ExtractUserId(identity);
            if (userId.HasValue)
                userIds.Add(userId.Value);
        }

        return userIds;
    }

    /// <summary>
    /// Verifies a deployment's credentials + reachability by listing rooms with a
    /// freshly-signed admin token. Used by the BYO-voice probe.
    /// </summary>
    public async Task<TaskResult> ProbeWithCredentialsAsync(LiveKitCredentials creds)
    {
        if (!creds.IsValid)
            return TaskResult.FromFailure("URL, API key, and API secret are all required.");

        var rooms = await ListRoomsAsync(creds);
        return rooms is null
            ? TaskResult.FromFailure("Could not reach the LiveKit server or the credentials were rejected.")
            : new TaskResult(true, "LiveKit server reachable and credentials accepted.");
    }

    // ================= Identity / room naming =================

    public static string RoomName(long channelId) => $"{RoomPrefix}{channelId}";

    private static long? ChannelIdFromRoom(string? roomName)
    {
        if (string.IsNullOrWhiteSpace(roomName) || !roomName.StartsWith(RoomPrefix, StringComparison.Ordinal))
            return null;

        return long.TryParse(roomName[RoomPrefix.Length..], out var id) ? id : null;
    }

    private static string BuildIdentity(long userId, string? sessionId)
    {
        var normalized = NormalizeSessionId(sessionId);
        return string.IsNullOrWhiteSpace(normalized) ? userId.ToString() : $"{userId}:{normalized}";
    }

    private static string NormalizeSessionId(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return string.Empty;

        // Keep the userId:sessionId shape parseable — the client interop splits on ':'.
        return sessionId.Trim().Replace(':', '_');
    }

    private static long? ExtractUserId(string? identity)
    {
        if (string.IsNullOrEmpty(identity))
            return null;

        var delimiter = identity.IndexOf(':');
        var candidate = delimiter > 0 ? identity[..delimiter] : identity;
        return long.TryParse(candidate, out var userId) ? userId : null;
    }

    private static string BuildParticipantMetadata(long channelId, long userId, string? sessionId)
    {
        var normalized = NormalizeSessionId(sessionId);
        return string.IsNullOrWhiteSpace(normalized)
            ? $"{{\"channelId\":\"{channelId}\",\"userId\":\"{userId}\"}}"
            : $"{{\"channelId\":\"{channelId}\",\"userId\":\"{userId}\",\"sessionId\":\"{normalized}\"}}";
    }

    // ================= Token signing =================

    /// <summary>
    /// Builds a signed LiveKit access token. Hand-rolled HS256 JWT so the nested
    /// <c>video</c> grant is serialized exactly as LiveKit expects, independent of
    /// how any JWT library flattens complex claims.
    /// </summary>
    private static string SignAccessToken(
        string apiKey,
        string apiSecret,
        string identity,
        string? name,
        string? metadata,
        Dictionary<string, object> videoGrant,
        TimeSpan lifetime)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var header = new Dictionary<string, object> { ["alg"] = "HS256", ["typ"] = "JWT" };
        var payload = new Dictionary<string, object>
        {
            ["iss"] = apiKey,
            ["sub"] = identity,
            ["nbf"] = now - 5,
            ["iat"] = now,
            ["exp"] = now + (long)lifetime.TotalSeconds,
            ["video"] = videoGrant,
        };

        if (!string.IsNullOrWhiteSpace(name))
            payload["name"] = name;
        if (!string.IsNullOrWhiteSpace(metadata))
            payload["metadata"] = metadata;

        var signingInput =
            Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(header, JsonOptions)) + "." +
            Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions));

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(apiSecret));
        var signature = hmac.ComputeHash(Encoding.UTF8.GetBytes(signingInput));

        return signingInput + "." + Base64UrlEncode(signature);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // ================= LiveKit RoomService (Twirp) =================

    private async Task<List<string>?> GetParticipantIdentitiesAsync(LiveKitCredentials creds, string room)
    {
        var response = await TwirpQueryAsync(creds, "ListParticipants", new { room }, "list participants");
        if (response is null)
            return null;

        var identities = new List<string>();
        if (response.Value.TryGetProperty("participants", out var participants) &&
            participants.ValueKind == JsonValueKind.Array)
        {
            foreach (var participant in participants.EnumerateArray())
            {
                if (participant.TryGetProperty("identity", out var identity) &&
                    identity.ValueKind == JsonValueKind.String)
                {
                    identities.Add(identity.GetString()!);
                }
            }
        }

        return identities;
    }

    public async Task<List<string>?> ListRoomsAsync(LiveKitCredentials creds)
    {
        var response = await TwirpQueryAsync(creds, "ListRooms", new { }, "list rooms");
        if (response is null)
            return null;

        var rooms = new List<string>();
        if (response.Value.TryGetProperty("rooms", out var roomArray) &&
            roomArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var room in roomArray.EnumerateArray())
            {
                if (room.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
                    rooms.Add(name.GetString()!);
            }
        }

        return rooms;
    }

    private Task RemoveParticipantAsync(LiveKitCredentials creds, string room, string identity) =>
        TwirpCommandAsync(creds, "RemoveParticipant", new { room, identity }, "remove participant", notFoundIsOk: true);

    /// <summary>Twirp call returning a parsed JSON body, or null on failure.</summary>
    private async Task<JsonElement?> TwirpQueryAsync(LiveKitCredentials creds, string method, object body, string operation)
    {
        try
        {
            using var response = await SendTwirpAsync(creds, method, body);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("LiveKit failed to {Operation}. Status {Status}: {Body}",
                    operation, (int)response.StatusCode, content);
                return null;
            }

            return string.IsNullOrWhiteSpace(content)
                ? default(JsonElement)
                : JsonSerializer.Deserialize<JsonElement>(content, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LiveKit request failed while trying to {Operation}", operation);
            return null;
        }
    }

    /// <summary>Twirp call for fire-and-forget commands; returns success.</summary>
    private async Task<bool> TwirpCommandAsync(
        LiveKitCredentials creds, string method, object body, string operation, bool notFoundIsOk = false)
    {
        try
        {
            using var response = await SendTwirpAsync(creds, method, body);
            if (!response.IsSuccessStatusCode)
            {
                // For idempotent teardown, "room/participant does not exist" already
                // satisfies the intent — don't treat it as a failure.
                if (notFoundIsOk && response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    return true;

                var content = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("LiveKit failed to {Operation}. Status {Status}: {Body}",
                    operation, (int)response.StatusCode, content);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LiveKit request failed while trying to {Operation}", operation);
            return false;
        }
    }

    private async Task<HttpResponseMessage> SendTwirpAsync(LiveKitCredentials creds, string method, object body)
    {
        var endpoint = $"{creds.ApiBase()}/twirp/livekit.RoomService/{method}";

        // Admin ops are room-scoped; ListRooms needs roomList. Grant both — the token
        // lives five minutes and never leaves the server.
        var grant = new Dictionary<string, object>
        {
            ["roomAdmin"] = true,
            ["roomList"] = true,
            ["roomCreate"] = true,
        };
        if (body.GetType().GetProperty("room")?.GetValue(body) is string room && !string.IsNullOrEmpty(room))
            grant["room"] = room;

        var adminToken = SignAccessToken(creds.ApiKey, creds.ApiSecret,
            "valour-server", null, null, grant, AdminTokenLifetime);

        var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var client = GetHttpClient(creds);
        return await client.SendAsync(request);
    }

    /// <summary>
    /// Instance SFU: plain pooled client (the operator configured it — trusted).
    /// External (BYO) SFU: SSRF-safe client that validates every resolved address
    /// at connect time, so an owner-supplied URL can't reach private ranges — the
    /// same discipline as federation fetches. AllowInsecurePlanetVoice (dev/LAN)
    /// lifts that restriction.
    /// </summary>
    private HttpClient GetHttpClient(LiveKitCredentials creds)
    {
        if (!creds.External)
            return _httpClientFactory.CreateClient();

        var allowPrivate = VoiceConfig.Current?.AllowInsecurePlanetVoice == true;
        return _externalHttpClient ??= new HttpClient(
            SsrfSafeConnect.CreateHandler(allowPrivate, acceptAnyCertificate: allowPrivate));
    }
}
