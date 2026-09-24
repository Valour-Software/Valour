using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Valour.Config.Configs;
using Valour.Server.Services;

namespace Valour.Tests.Services;

public class ReleaseVoiceCleanupTests
{
    public ReleaseVoiceCleanupTests() => _ = new CloudflareConfig
    {
        RealtimeAccountId = "test-account", RealtimeAppId = "test-app", RealtimeApiToken = "test-token"
    };

    [Fact]
    public async Task FailedKick_IsNotReportedClosedAndKeepsTrackedMapping()
    {
        var handler = new Handler { FailKick = true };
        var service = Create(handler, new Clock());
        service.TrackMeetingMapping(42, "meeting");
        await service.CloseTrackedMeetingAsync(42, "empty channel");
        Assert.Equal("meeting", service.GetTrackedChannelMeetingIds()[42]);
        Assert.False((await service.CloseMeetingAsync("meeting", "retry")).Success);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains(handler.Requests, x => x.Method == HttpMethod.Patch);
        Assert.True(handler.EmptyKickBody);
    }

    [Fact]
    public async Task FailedCleanup_BacksOffAndCanRecover()
    {
        var handler = new Handler { FailKick = true };
        var clock = new Clock();
        var service = Create(handler, clock);
        Assert.False((await service.CloseMeetingAsync("meeting", "test")).Success);
        Assert.False((await service.CloseMeetingAsync("meeting", "test")).Success);
        Assert.Equal(2, handler.Requests.Count);
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.False((await service.CloseMeetingAsync("meeting", "test")).Success);
        Assert.Equal(4, handler.Requests.Count);
        handler.FailKick = false;
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.False((await service.CloseMeetingAsync("meeting", "test")).Success);
        Assert.Equal(4, handler.Requests.Count);
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True((await service.CloseMeetingAsync("meeting", "test")).Success);
        Assert.Equal(6, handler.Requests.Count);
        Assert.True((await service.CloseMeetingAsync("meeting", "stale reporting")).Success);
        Assert.Equal(6, handler.Requests.Count);
    }

    [Fact]
    public async Task FailedDisable_IsNotReportedClosed()
    {
        var handler = new Handler { FailDisable = true };
        Assert.False((await Create(handler, new Clock()).CloseMeetingAsync("meeting", "test")).Success);
    }

    [Fact]
    public async Task ConcurrentCleanup_UsesOneProviderOperation()
    {
        var handler = new Handler { KickGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var service = Create(handler, new Clock());
        var first = service.CloseMeetingAsync("meeting", "test");
        var second = service.CloseMeetingAsync("meeting", "test");
        handler.KickGate.SetResult();
        Assert.All(await Task.WhenAll(first, second), x => Assert.True(x.Success));
        Assert.Equal(2, handler.Requests.Count);
    }

    private static RealtimeKitService Create(Handler handler, Clock clock) => new(new Factory(handler), NullLogger<RealtimeKitService>.Instance, new EmptyServices(), clock);
    internal sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
    internal sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, false);
    }
    private sealed class EmptyServices : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
    internal sealed class Handler : HttpMessageHandler
    {
        public bool FailKick, FailDisable, EmptyKickBody;
        public TaskCompletionSource? KickGate;
        public List<(HttpMethod Method, string Path)> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var kick = request.RequestUri!.AbsolutePath.EndsWith("kick-all");
            Requests.Add((request.Method, request.RequestUri.AbsolutePath));
            if (kick)
            {
                EmptyKickBody = request.Content is null;
                if (KickGate is not null) await KickGate.Task;
            }
            var failed = kick ? FailKick : FailDisable;
            return new HttpResponseMessage(failed ? HttpStatusCode.InternalServerError : HttpStatusCode.OK)
            {
                Content = new StringContent(failed ? """{"success":false,"error":{"code":500,"message":"Internal Server Error"}}""" : """{"success":true,"data":{}}""", Encoding.UTF8, "application/json")
            };
        }
    }
}
