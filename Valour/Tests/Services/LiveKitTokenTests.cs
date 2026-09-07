using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Valour.Server.Services;

namespace Valour.Tests.Services;

public class LiveKitTokenTests
{
    private static readonly LiveKitCredentials Credentials = new(
        "wss://voice.example.test",
        "https://voice.example.test",
        "test-key",
        "01234567890123456789012345678901",
        External: false);

    [Fact]
    public void ParticipantToken_UsesRequestedLifetimeAndNormalizesSessionIdentity()
    {
        var service = new LiveKitService(
            new UnusedHttpClientFactory(),
            NullLogger<LiveKitService>.Instance);

        var response = service.CreateParticipantTokenWithCredentials(
            Credentials,
            channelId: 42,
            userId: 123,
            displayName: "Caller",
            sessionId: " browser:one ",
            tokenLifetime: TimeSpan.FromMinutes(5));

        using var payload = DecodePayload(response.AuthToken);
        var issuedAt = payload.RootElement.GetProperty("iat").GetInt64();
        var expiresAt = payload.RootElement.GetProperty("exp").GetInt64();
        var grant = payload.RootElement.GetProperty("video");

        Assert.Equal(300, expiresAt - issuedAt);
        Assert.Equal("123:browser_one", payload.RootElement.GetProperty("sub").GetString());
        Assert.Equal("valour-42", response.MeetingId);
        Assert.Equal("valour-42", grant.GetProperty("room").GetString());
        Assert.True(grant.GetProperty("roomJoin").GetBoolean());
        Assert.True(grant.GetProperty("canPublish").GetBoolean());
        Assert.True(grant.GetProperty("canSubscribe").GetBoolean());
    }

    private static JsonDocument DecodePayload(string jwt)
    {
        var encoded = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
        encoded = encoded.PadRight(encoded.Length + ((4 - encoded.Length % 4) % 4), '=');
        return JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(encoded)));
    }

    private sealed class UnusedHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            throw new InvalidOperationException("Token creation must not perform network I/O.");
    }
}
