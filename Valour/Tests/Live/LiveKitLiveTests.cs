using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Valour.Config.Configs;
using Valour.Server.Models;
using Valour.Server.Services;
using Valour.Shared.Models;

namespace Valour.Tests.Live;

/// <summary>
/// Drives the real <see cref="LiveKitService"/> against a LIVE livekit-server
/// container. Skipped unless LIVE_LIVEKIT=1, so it never runs in CI — it needs a
/// LiveKit SFU up (docker run livekit/livekit-server --dev).
///
/// Env: LIVE_LIVEKIT=1, LIVEKIT_URL (default ws://localhost:7880),
/// LIVEKIT_KEY (default devkey), LIVEKIT_SECRET.
///
/// Shares the "VoiceProviderState" collection so it never runs in parallel with
/// other tests that mutate the global VoiceConfig.Current singleton.
/// </summary>
[Collection("VoiceProviderState")]
public class LiveKitLiveTests
{
    private static bool Enabled => Environment.GetEnvironmentVariable("LIVE_LIVEKIT") == "1";

    private static string Env(string key, string fallback) =>
        Environment.GetEnvironmentVariable(key) is { Length: > 0 } v ? v : fallback;

    private sealed class SimpleHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    [Fact]
    public async Task LiveKitService_MintsTokens_AndDrivesRoomService()
    {
        Assert.SkipUnless(Enabled, "Set LIVE_LIVEKIT=1 with a livekit-server container up.");

        var wsUrl = Env("LIVEKIT_URL", "ws://localhost:7880");
        var key = Env("LIVEKIT_KEY", "devkey");
        var secret = Env("LIVEKIT_SECRET", "valour-livekit-dev-secret-000000000000");
        var httpBase = wsUrl.Replace("ws://", "http://").Replace("wss://", "https://").TrimEnd('/');

        // VoiceConfig ctor installs itself as the singleton the service reads.
        _ = new VoiceConfig
        {
            Provider = "livekit",
            LiveKitUrl = wsUrl,
            LiveKitApiKey = key,
            LiveKitApiSecret = secret,
        };

        var service = new LiveKitService(new SimpleHttpClientFactory(), NullLogger<LiveKitService>.Instance);
        Assert.True(service.IsConfigured, "service should report configured");
        Assert.Equal(VoiceProvider.LiveKit, service.Kind);

        var channel = new Channel { Id = 999001 };
        const long userId = 424242;

        // 1. The service mints a real join token.
        var tokenResult = await service.CreateParticipantTokenAsync(channel, userId, "Tester#0001", "sessionA");
        Assert.True(tokenResult.Success, "token mint: " + tokenResult.Message);
        var data = tokenResult.Data!;
        Assert.Equal(VoiceProviderExtensions.LiveKitWire, data.Provider);
        Assert.Equal(wsUrl, data.Url);
        Assert.Equal(LiveKitService.RoomName(channel.Id), data.MeetingId);
        Assert.Equal($"{userId}:sessionA", data.ParticipantId);
        Assert.False(string.IsNullOrWhiteSpace(data.AuthToken));

        // 2. The token verifies structurally (HS256 over the secret) and carries
        //    the expected join grant.
        AssertValidLiveKitJoinToken(data.AuthToken, key, secret, data.MeetingId, data.ParticipantId);

        // 3. The SFU itself accepts the service-minted join token for connection.
        using var http = new HttpClient();
        var validateResp = await http.GetAsync($"{httpBase}/rtc/validate?access_token={Uri.EscapeDataString(data.AuthToken)}");
        var validateBody = await validateResp.Content.ReadAsStringAsync();
        Assert.True(validateResp.IsSuccessStatusCode, $"SFU rejected join token ({(int)validateResp.StatusCode}): {validateBody}");

        // 4. The admin Twirp path works: ListParticipants (via GetConnectedUserIds)
        //    returns a non-null set (empty room), proving the server-signed admin
        //    token is accepted by RoomService.
        var connected = await service.GetConnectedUserIdsAsync(channel.Id, data.MeetingId);
        Assert.NotNull(connected);
        Assert.Empty(connected!);

        // 5. ListRooms (via LoadOpenMeetingMappings) also authenticates and returns.
        var mappings = await service.LoadOpenMeetingMappingsAsync();
        Assert.NotNull(mappings);

        // 6. Teardown is idempotent: closing a room that was never actually created
        //    (no client joined) reports success rather than a spurious failure — the
        //    desired end state (room gone) already holds. This is the path the cleanup
        //    worker hits for auto-expired rooms.
        var close = await service.CloseMeetingAsync(data.MeetingId, "test teardown");
        Assert.True(close.Success, "close room: " + close.Message);
    }

    /// <summary>
    /// The bring-your-own-voice path: planet-scoped (External) credentials mint a
    /// token the owner's SFU accepts, mark the response SelfHosted, and drive the
    /// admin API through the SSRF-safe external connect path (allowed here via
    /// AllowInsecurePlanetVoice because the test SFU is on localhost).
    /// </summary>
    [Fact]
    public async Task PlanetCredentials_MintSelfHostedTokens_AndDriveOwnerSfu()
    {
        Assert.SkipUnless(Enabled, "Set LIVE_LIVEKIT=1 with a livekit-server container up.");

        var wsUrl = Env("LIVEKIT_URL", "ws://localhost:7880");
        var key = Env("LIVEKIT_KEY", "devkey");
        var secret = Env("LIVEKIT_SECRET", "valour-livekit-dev-secret-000000000000");
        var httpBase = wsUrl.Replace("ws://", "http://").Replace("wss://", "https://").TrimEnd('/');

        // The instance runs with NO LiveKit of its own — planet creds are the only
        // route, as on the official deployment. AllowInsecurePlanetVoice lets the
        // SSRF-safe handler reach the localhost test SFU.
        _ = new VoiceConfig { AllowInsecurePlanetVoice = true };

        var service = new LiveKitService(new SimpleHttpClientFactory(), NullLogger<LiveKitService>.Instance);
        Assert.False(service.IsConfigured, "instance-level LiveKit should NOT be configured in this scenario");

        var planetCreds = new LiveKitCredentials(wsUrl, null, key, secret, External: true);
        const long channelId = 999002;
        const long userId = 512512;

        // 1. Token mint with planet creds is marked SelfHosted and carries the owner's URL.
        var response = service.CreateParticipantTokenWithCredentials(planetCreds, channelId, userId, "Owner#0001", "sessB");
        Assert.True(response.SelfHosted, "planet-credentialed tokens must be flagged SelfHosted");
        Assert.Equal(VoiceProviderExtensions.LiveKitWire, response.Provider);
        Assert.Equal(wsUrl, response.Url);
        AssertValidLiveKitJoinToken(response.AuthToken, key, secret, response.MeetingId, response.ParticipantId);

        // 2. The owner's SFU accepts the token.
        using var http = new HttpClient();
        var validateResp = await http.GetAsync($"{httpBase}/rtc/validate?access_token={Uri.EscapeDataString(response.AuthToken)}");
        Assert.True(validateResp.IsSuccessStatusCode, "owner SFU rejected the planet-credentialed join token");

        // 3. Admin ops (probe + participant listing) work through the External
        //    (SSRF-safe) HTTP path.
        var probe = await service.ProbeWithCredentialsAsync(planetCreds);
        Assert.True(probe.Success, "probe: " + probe.Message);

        var connected = await service.GetConnectedUserIdsWithCredentialsAsync(planetCreds, response.MeetingId);
        Assert.NotNull(connected);

        // 4. Bad credentials are rejected by the SFU (probe reports failure).
        var badCreds = planetCreds with { ApiSecret = "wrong-secret-wrong-secret-wrong-0000" };
        var badProbe = await service.ProbeWithCredentialsAsync(badCreds);
        Assert.False(badProbe.Success, "probe with a wrong secret must fail");

        // 5. Teardown via planet creds is idempotent.
        var close = await service.CloseMeetingWithCredentialsAsync(planetCreds, response.MeetingId, "test teardown");
        Assert.True(close.Success, "close room: " + close.Message);
    }

    [Fact]
    public void ApiBase_DerivesFromWebsocketUrl()
    {
        Assert.Equal("https://voice.example.com", new LiveKitCredentials("wss://voice.example.com/", null, "k", "s", true).ApiBase());
        Assert.Equal("http://localhost:7880", new LiveKitCredentials("ws://localhost:7880", null, "k", "s", false).ApiBase());
        Assert.Equal("http://internal:7880", new LiveKitCredentials("wss://public.example.com", "http://internal:7880/", "k", "s", false).ApiBase());
    }

    private static void AssertValidLiveKitJoinToken(
        string token, string expectedIss, string secret, string expectedRoom, string expectedSub)
    {
        var parts = token.Split('.');
        Assert.Equal(3, parts.Length);

        // Signature check: HMAC-SHA256 over "header.payload".
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var expectedSig = Base64Url(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{parts[0]}.{parts[1]}")));
        Assert.Equal(expectedSig, parts[2]);

        using var payload = JsonDocument.Parse(Base64UrlDecode(parts[1]));
        var root = payload.RootElement;
        Assert.Equal(expectedIss, root.GetProperty("iss").GetString());
        Assert.Equal(expectedSub, root.GetProperty("sub").GetString());

        var video = root.GetProperty("video");
        Assert.True(video.GetProperty("roomJoin").GetBoolean());
        Assert.Equal(expectedRoom, video.GetProperty("room").GetString());
        Assert.True(video.GetProperty("canPublish").GetBoolean());
        Assert.True(video.GetProperty("canSubscribe").GetBoolean());
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string input)
    {
        var s = input.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
}
