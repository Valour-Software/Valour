using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Valour.Config.Configs;
using Valour.Server.Services;

namespace Valour.Tests.Services;

/// <summary>
/// Regression tests for the voice reconciliation data source. Cloudflare's sessions
/// API stopped recording sessions (success + empty list) while calls had live
/// participants, and reconciliation kicked every participant of every active call
/// each pass. GetConnectedUserIdsAsync must return null — "cannot verify" — for any
/// answer that lacks positive evidence of who is connected.
/// </summary>
public class RealtimeKitReconciliationTests
{
    private const string EmptySessionsBody =
        """{"success":true,"data":{"sessions":[]},"paging":{"total_count":0,"start_offset":1,"end_offset":0}}""";

    private const string OneLiveSessionBody =
        """{"success":true,"data":{"sessions":[{"id":"sess-1","associated_id":"meet-1","created_at":"2026-07-26T19:17:27.646Z","status":"LIVE","live_participants":2}]}}""";

    public RealtimeKitReconciliationTests()
    {
        // The service reads config from the static instance; the ctor assigns it.
        _ = new CloudflareConfig
        {
            RealtimeAccountId = "test-account",
            RealtimeAppId = "test-app",
            RealtimeApiToken = "test-token"
        };
    }

    [Fact]
    public async Task EmptyLiveSessionList_ReturnsNull()
    {
        // The exact prod failure: success:true with zero sessions for a meeting
        // that had three connected users.
        var service = CreateService(new RouteHandler
        {
            ["/sessions?"] = (HttpStatusCode.OK, EmptySessionsBody)
        });

        var result = await service.GetConnectedUserIdsAsync(1, "meet-1");

        Assert.Null(result);
    }

    [Fact]
    public async Task SessionsQueryFailure_ReturnsNull()
    {
        var service = CreateService(new RouteHandler
        {
            ["/sessions?"] = (HttpStatusCode.InternalServerError,
                """{"success":false,"errors":[{"code":500,"message":"boom"}]}""")
        });

        var result = await service.GetConnectedUserIdsAsync(1, "meet-1");

        Assert.Null(result);
    }

    [Fact]
    public async Task ParticipantsQueryFailure_ReturnsNull()
    {
        // A session exists but its participant list cannot be fetched; partial data
        // must not read as "those users left".
        var service = CreateService(new RouteHandler
        {
            ["/sessions/sess-1/participants"] = (HttpStatusCode.InternalServerError,
                """{"success":false,"errors":[{"code":500,"message":"boom"}]}"""),
            ["/sessions?"] = (HttpStatusCode.OK, OneLiveSessionBody)
        });

        var result = await service.GetConnectedUserIdsAsync(1, "meet-1");

        Assert.Null(result);
    }

    [Fact]
    public async Task AllParticipantsLeft_ReturnsNull()
    {
        var service = CreateService(new RouteHandler
        {
            ["/sessions/sess-1/participants"] = (HttpStatusCode.OK,
                """{"success":true,"data":{"participants":[{"id":"p1","custom_participant_id":"123:abc","left_at":"2026-07-26T19:18:00.000Z"}]}}"""),
            ["/sessions?"] = (HttpStatusCode.OK, OneLiveSessionBody)
        });

        var result = await service.GetConnectedUserIdsAsync(1, "meet-1");

        Assert.Null(result);
    }

    [Fact]
    public async Task ConnectedParticipants_ReturnsTheirUserIds()
    {
        var service = CreateService(new RouteHandler
        {
            ["/sessions/sess-1/participants"] = (HttpStatusCode.OK,
                """{"success":true,"data":{"participants":[{"id":"p1","custom_participant_id":"123:abc","left_at":null},{"id":"p2","custom_participant_id":"456","left_at":null},{"id":"p3","custom_participant_id":"789","left_at":"2026-07-26T19:18:00.000Z"}]}}"""),
            ["/sessions?"] = (HttpStatusCode.OK, OneLiveSessionBody)
        });

        var result = await service.GetConnectedUserIdsAsync(1, "meet-1");

        Assert.NotNull(result);
        Assert.Equal(new HashSet<long> { 123, 456 }, result);
    }

    [Fact]
    public async Task KickSpecificSession_DeletesParticipantRecordSoItsTokenCannotRejoin()
    {
        var handler = new RouteHandler
        {
            ["/active-session/kick"] = (HttpStatusCode.OK, """{"success":true,"data":{}}"""),
            ["/meetings/meet-1/participants?"] = (HttpStatusCode.OK,
                """{"success":true,"data":[{"id":"record-1","custom_participant_id":"123:session-a"},{"id":"record-2","custom_participant_id":"123:session-b"}]}"""),
            ["/meetings/meet-1/participants/record-1"] = (HttpStatusCode.OK,
                """{"success":true,"data":{"custom_participant_id":"123:session-a"}}""")
        };
        var service = CreateService(handler);
        service.TrackMeetingMapping(42, "meet-1");

        await service.KickUserSessionFromTrackedChannelAsync(42, 123, "session-a");

        Assert.Contains(handler.Requests, request =>
            request.Method == HttpMethod.Post && request.Uri.Contains("/active-session/kick"));
        Assert.Contains(handler.Requests, request =>
            request.Method == HttpMethod.Delete && request.Uri.Contains("/participants/record-1"));
        Assert.DoesNotContain(handler.Requests, request =>
            request.Method == HttpMethod.Delete && request.Uri.Contains("/participants/record-2"));
    }

    [Fact]
    public async Task ParticipantQueries_UseSupportedPageSizeAndIncludeLaterPages()
    {
        var firstPage = System.Text.Json.JsonSerializer.Serialize(new
        {
            success = true,
            data = new { participants = Enumerable.Range(1, 200).Select(i => new { id = $"p{i}", custom_participant_id = i.ToString() }) }
        });
        var handler = new RouteHandler
        {
            ["page_no=1"] = (HttpStatusCode.OK, firstPage),
            ["page_no=2"] = (HttpStatusCode.OK, """{"success":true,"data":{"participants":[{"id":"p201","custom_participant_id":"201"}]}}""")
        };
        var result = await CreateService(handler).GetSessionParticipantsAsync("sess-1");
        Assert.True(result.Success, result.Message);
        Assert.Equal(201, result.Data.Count);
        Assert.All(handler.Requests, x => Assert.Contains("per_page=200", x.Uri));
    }

    [Fact]
    public async Task ParticipantQueries_RepeatedPageDoesNotReturnPartialSuccess()
    {
        var body = System.Text.Json.JsonSerializer.Serialize(new
        {
            success = true,
            data = new { participants = Enumerable.Range(1, 200).Select(i => new { id = $"p{i}", custom_participant_id = i.ToString() }) }
        });
        var service = CreateService(new RouteHandler { ["/participants?"] = (HttpStatusCode.OK, body) });
        var result = await service.GetSessionParticipantsAsync("sess-1");
        Assert.False(result.Success);
        Assert.Null(result.Data);
    }

    private static RealtimeKitService CreateService(RouteHandler handler) =>
        new(new StubHttpClientFactory(handler),
            NullLogger<RealtimeKitService>.Instance,
            new EmptyServiceProvider());

    /// <summary>
    /// Routes requests by first matching URL substring; unmatched requests fail the test.
    /// </summary>
    private sealed class RouteHandler : HttpMessageHandler
    {
        private readonly List<(string Fragment, HttpStatusCode Status, string Body)> _routes = new();
        public List<(HttpMethod Method, string Uri)> Requests { get; } = [];

        public (HttpStatusCode, string) this[string urlFragment]
        {
            set => _routes.Add((urlFragment, value.Item1, value.Item2));
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            Requests.Add((request.Method, url));
            foreach (var (fragment, status, body) in _routes)
            {
                if (url.Contains(fragment, StringComparison.Ordinal))
                {
                    return Task.FromResult(new HttpResponseMessage(status)
                    {
                        Content = new StringContent(body, Encoding.UTF8, "application/json")
                    });
                }
            }

            throw new InvalidOperationException($"Unexpected request in test: {url}");
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
