using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Valour.Client.Components.Calls;
using Valour.Sdk.Client;

namespace Valour.Tests.Client;

public class NearbyCallRenderingTests
{
    [Fact]
    public async Task NearbyList_FiltersAvatarsButKeepsAudioHosts()
    {
        var html = await RenderAsync([
            new() { PeerId = "self", UserId = "1", Name = "Me", IsSelf = true },
            new() { PeerId = "near", UserId = "2", Name = "Nearby friend", AudioEnabled = true },
            new() { PeerId = "far", UserId = "3", Name = "Distant friend" }
        ], "near");

        Assert.Contains("alt=\"Nearby friend\"", html);
        Assert.DoesNotContain("alt=\"Me\"", html);
        Assert.DoesNotContain("alt=\"Distant friend\"", html);
        Assert.Contains("nearby-person speaking", html);
        Assert.Equal(3, html.Split("<audio ").Length - 1);
        Assert.DoesNotContain("video-stage", html);
        Assert.DoesNotContain("call-header", html);
    }

    [Theory]
    [InlineData(false, false, false, 0)]
    [InlineData(true, false, false, 0)]
    [InlineData(true, true, false, 1)]
    [InlineData(true, true, true, 2)]
    public async Task VideoButtons_RequireAvailableTracks(bool enabled, bool camera, bool screen, int buttons)
    {
        var html = await RenderAsync([
            new() { PeerId = "near", UserId = "2", Name = "Friend", VideoEnabled = enabled,
                HasVideoTrack = camera, HasScreenShareTrack = screen }
        ]);
        Assert.Equal(buttons, html.Split("class=\"nearby-video-button ").Length - 1);
        Assert.DoesNotContain("<video ", html);
    }

    [Fact]
    public async Task EmptyNearbyCall_HasNoWaitingPanel()
    {
        var html = await RenderAsync([]);
        Assert.Contains("Call controls", html);
        Assert.DoesNotContain("Waiting for", html);
        Assert.DoesNotContain("video-empty", html);
    }

    private static async Task<string> RenderAsync(RealtimeKitParticipantState[] participants, string? speaker = null)
    {
        var client = new ValourClient("http://localhost:59999/");
        var host = new RealtimeKitHostService();
        await using var session = new GlobalCallSessionService(client, host);
        typeof(GlobalCallSessionService).GetProperty(nameof(session.ParticipantsSnapshot))!.SetValue(session,
            new RealtimeKitParticipantsSnapshot { Participants = participants, ActiveSpeakerPeerId = speaker });
        await using var services = new ServiceCollection().AddLogging()
            .AddSingleton(client).AddSingleton(host).AddSingleton(session).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<CallPanelComponent>(ParameterView.FromDictionary(
                new Dictionary<string, object>
                {
                    [nameof(CallPanelComponent.NearbyMode)] = true,
                    [nameof(CallPanelComponent.VideoMode)] = true,
                    [nameof(CallPanelComponent.IsNearbyUser)] = (Func<long, bool>)(id => id == 2)
                }));
            return output.ToHtmlString();
        });
    }
}
