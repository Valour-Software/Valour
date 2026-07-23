using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Valour.Database.Context;
using Valour.Sdk.Models;
using Valour.Sdk.Models.Embeds;
using Valour.Shared.Models;

namespace Valour.Tests.Apis;

/// <summary>
/// End-to-end tests for planet webhooks against the real server: management
/// API, anonymous token-URL execution, overrides, spoof protection, token
/// rotation, message edit/delete, and rate limiting.
/// </summary>
[Collection("ApiCollection")]
public class PlanetWebhookApiLiveTests : IAsyncLifetime
{
    private readonly LoginTestFixture _fixture;

    private Valour.Sdk.Models.Planet _planet = null!;
    private Valour.Sdk.Models.Channel _channel = null!;
    private HttpClient _anonymous = null!;

    public PlanetWebhookApiLiveTests(LoginTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync()
    {
        var create = await new Valour.Sdk.Models.Planet(_fixture.Client)
        {
            Name = $"Webhook E2E {Guid.NewGuid().ToString()[..8]}",
            Description = "Webhook test planet",
        }.CreateAsync();
        Assert.True(create.Success, create.Message);

        _planet = await _fixture.Client.PlanetService.FetchPlanetAsync(create.Data.Id, skipCache: true);
        _channel = await _planet.FetchPrimaryChatChannelAsync();
        Assert.NotNull(_channel);

        // A client with no authorization header at all
        _anonymous = _fixture.Factory.CreateClient();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private async Task<PlanetWebhook> CreateWebhookAsync(string name = "Test Hook")
    {
        var response = await _fixture.Client.Http.PostAsJsonAsync("api/planetwebhooks", new
        {
            PlanetId = _planet.Id,
            ChannelId = _channel.Id,
            Name = name,
        });

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var webhook = await response.Content.ReadFromJsonAsync<PlanetWebhook>();
        Assert.NotNull(webhook);
        Assert.NotNull(webhook.Token);
        Assert.StartsWith("whk_", webhook.Token);
        return webhook;
    }

    private async Task WaitForPersistedAsync(long messageId)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ValourDb>();

        for (var i = 0; i < 60; i++)
        {
            if (await db.Messages.AnyAsync(x => x.Id == messageId))
                return;
            await Task.Delay(500);
        }

        Assert.Fail("Message was not persisted in time.");
    }

    [Fact]
    public async Task Webhook_FullLifecycle()
    {
        // ---- Create (owner has ManageWebhooks via full control) ----
        var webhook = await CreateWebhookAsync();
        Assert.Equal(_planet.Id, webhook.PlanetId);
        Assert.Equal(_channel.Id, webhook.ChannelId);

        var executeUrl = ISharedPlanetWebhook.GetExecuteRoute(webhook.Id, webhook.Token!);

        // ---- Anonymous info fetch does not echo the token ----
        var info = await _anonymous.GetFromJsonAsync<PlanetWebhook>(executeUrl);
        Assert.NotNull(info);
        Assert.Equal(webhook.Name, info.Name);
        Assert.Null(info.Token);

        // ---- Execute anonymously with defaults ----
        var plainResponse = await _anonymous.PostAsJsonAsync(executeUrl, new WebhookExecuteRequest
        {
            Content = "Hello from webhook",
        });
        Assert.True(plainResponse.IsSuccessStatusCode, await plainResponse.Content.ReadAsStringAsync());

        var plainMessage = await plainResponse.Content.ReadFromJsonAsync<Valour.Sdk.Models.Message>();
        Assert.NotNull(plainMessage);
        Assert.Equal(webhook.Id, plainMessage.WebhookId);
        Assert.Equal("Test Hook", plainMessage.OverrideName); // webhook default stamped
        Assert.Equal(ISharedUser.VictorUserId, plainMessage.AuthorUserId);
        Assert.Null(plainMessage.AuthorMemberId);
        Assert.Equal(_channel.Id, plainMessage.ChannelId);

        // ---- Execute with overrides + embed ----
        var embed = new EmbedBuilder()
            .AddPage("Webhook Embed")
                .AddText("Sent through a webhook")
            .Build();

        var overrideResponse = await _anonymous.PostAsJsonAsync(executeUrl, new WebhookExecuteRequest
        {
            Content = "With overrides",
            OverrideName = "CI Bot",
            OverrideAvatarUrl = "https://example.com/avatar.png",
        }.WithEmbed(embed));
        Assert.True(overrideResponse.IsSuccessStatusCode, await overrideResponse.Content.ReadAsStringAsync());

        var overrideMessage = await overrideResponse.Content.ReadFromJsonAsync<Valour.Sdk.Models.Message>();
        Assert.NotNull(overrideMessage);
        Assert.Equal("CI Bot", overrideMessage.OverrideName);
        Assert.Equal("https://example.com/avatar.png", overrideMessage.OverrideAvatarUrl);
        Assert.NotNull(overrideMessage.EmbedAttachment?.Embed);
        Assert.Equal("Webhook Embed", overrideMessage.EmbedAttachment.Embed.Pages[0].Title);

        // ---- Reply-to must be in the webhook's channel ----
        var replyResponse = await _anonymous.PostAsJsonAsync(executeUrl, new WebhookExecuteRequest
        {
            Content = "replying",
            ReplyToId = plainMessage.Id,
        });
        Assert.True(replyResponse.IsSuccessStatusCode, await replyResponse.Content.ReadAsStringAsync());

        var badReplyResponse = await _anonymous.PostAsJsonAsync(executeUrl, new WebhookExecuteRequest
        {
            Content = "replying to nothing",
            ReplyToId = 12345,
        });
        Assert.False(badReplyResponse.IsSuccessStatusCode);

        // ---- Bad token / unknown id are rejected ----
        var badToken = await _anonymous.PostAsJsonAsync(
            ISharedPlanetWebhook.GetExecuteRoute(webhook.Id, "whk_wrongwrongwrongwrong"),
            new WebhookExecuteRequest { Content = "nope" });
        Assert.Equal(HttpStatusCode.Forbidden, badToken.StatusCode);

        var badId = await _anonymous.PostAsJsonAsync(
            ISharedPlanetWebhook.GetExecuteRoute(12345, webhook.Token!),
            new WebhookExecuteRequest { Content = "nope" });
        Assert.Equal(HttpStatusCode.Forbidden, badId.StatusCode);

        // ---- Unsafe override avatar is rejected ----
        var httpAvatar = await _anonymous.PostAsJsonAsync(executeUrl, new WebhookExecuteRequest
        {
            Content = "bad avatar",
            OverrideAvatarUrl = "http://insecure.example/a.png",
        });
        Assert.False(httpAvatar.IsSuccessStatusCode);

        // ---- Edit via token ----
        await WaitForPersistedAsync(plainMessage.Id);

        var editResponse = await _anonymous.PutAsJsonAsync($"{executeUrl}/messages/{plainMessage.Id}",
            new WebhookMessageEditRequest { Content = "Edited by webhook" });
        Assert.True(editResponse.IsSuccessStatusCode, await editResponse.Content.ReadAsStringAsync());

        var edited = await editResponse.Content.ReadFromJsonAsync<Valour.Sdk.Models.Message>();
        Assert.Equal("Edited by webhook", edited!.Content);
        Assert.Equal(webhook.Id, edited.WebhookId); // identity preserved on edit
        Assert.Equal("Test Hook", edited.OverrideName);

        // ---- Cannot edit a non-webhook message via token ----
        var normalSend = await _channel.SendMessageAsync("a normal user message");
        Assert.True(normalSend.Success, normalSend.Message);

        var editForeign = await _anonymous.PutAsJsonAsync($"{executeUrl}/messages/{normalSend.Data.Id}",
            new WebhookMessageEditRequest { Content = "hijacked" });
        Assert.False(editForeign.IsSuccessStatusCode);

        // ---- Delete via token (even though another message replies to it) ----
        var deleteResponse = await _anonymous.DeleteAsync($"{executeUrl}/messages/{plainMessage.Id}");
        Assert.True(deleteResponse.IsSuccessStatusCode, await deleteResponse.Content.ReadAsStringAsync());

        // ---- Rotate invalidates the old token ----
        var rotateResponse = await _fixture.Client.Http.PostAsync($"api/planetwebhooks/{webhook.Id}/rotate", null);
        Assert.True(rotateResponse.IsSuccessStatusCode, await rotateResponse.Content.ReadAsStringAsync());
        var rotated = await rotateResponse.Content.ReadFromJsonAsync<PlanetWebhook>();
        Assert.NotNull(rotated!.Token);
        Assert.NotEqual(webhook.Token, rotated.Token);

        var oldTokenResponse = await _anonymous.PostAsJsonAsync(executeUrl, new WebhookExecuteRequest { Content = "stale" });
        Assert.Equal(HttpStatusCode.Forbidden, oldTokenResponse.StatusCode);

        var newTokenResponse = await _anonymous.PostAsJsonAsync(
            ISharedPlanetWebhook.GetExecuteRoute(webhook.Id, rotated.Token!),
            new WebhookExecuteRequest { Content = "fresh token works" });
        Assert.True(newTokenResponse.IsSuccessStatusCode, await newTokenResponse.Content.ReadAsStringAsync());

        // ---- Management list + delete ----
        var list = await _fixture.Client.Http.GetFromJsonAsync<List<PlanetWebhook>>($"api/planets/{_planet.Id}/webhooks");
        Assert.NotNull(list);
        Assert.Contains(list, x => x.Id == webhook.Id);

        var deleteWebhook = await _fixture.Client.Http.DeleteAsync($"api/planetwebhooks/{webhook.Id}");
        Assert.True(deleteWebhook.IsSuccessStatusCode);

        var afterDelete = await _anonymous.PostAsJsonAsync(
            ISharedPlanetWebhook.GetExecuteRoute(webhook.Id, rotated.Token!),
            new WebhookExecuteRequest { Content = "gone" });
        Assert.Equal(HttpStatusCode.Forbidden, afterDelete.StatusCode);
    }

    [Fact]
    public async Task NormalMessagePost_CannotSpoofWebhookIdentity()
    {
        await _planet.EnsureReadyAsync();

        var message = new Valour.Sdk.Models.Message(_fixture.Client)
        {
            Content = "spoof attempt",
            ChannelId = _channel.Id,
            PlanetId = _planet.Id,
            AuthorUserId = _fixture.Client.Me.Id,
            AuthorMemberId = _planet.MyMember?.Id,
            Fingerprint = Guid.NewGuid().ToString(),
            WebhookId = 999,
            OverrideName = "Fake Admin",
            OverrideAvatarUrl = "https://example.com/fake.png",
        };

        var result = await _fixture.Client.MessageService.SendMessage(message);
        Assert.True(result.Success, result.Message);

        Assert.Null(result.Data.WebhookId);
        Assert.Null(result.Data.OverrideName);
        Assert.Null(result.Data.OverrideAvatarUrl);
    }

    [Fact]
    public async Task Webhook_CreateValidation()
    {
        // Wrong-planet channel binding is rejected: bind to a channel id
        // that isn't in the planet
        var response = await _fixture.Client.Http.PostAsJsonAsync("api/planetwebhooks", new
        {
            PlanetId = _planet.Id,
            ChannelId = 12345L,
            Name = "Bad Channel",
        });
        Assert.False(response.IsSuccessStatusCode);

        // Name is required
        var noName = await _fixture.Client.Http.PostAsJsonAsync("api/planetwebhooks", new
        {
            PlanetId = _planet.Id,
            ChannelId = _channel.Id,
        });
        Assert.False(noName.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Webhook_RateLimit_Returns429OnBurst()
    {
        var webhook = await CreateWebhookAsync("Burst Hook");
        var executeUrl = ISharedPlanetWebhook.GetExecuteRoute(webhook.Id, webhook.Token!);

        var got429 = false;
        HttpResponseMessage last = null!;

        // The window allows 30/min; drive past it
        for (var i = 0; i < 35 && !got429; i++)
        {
            last = await _anonymous.PostAsJsonAsync(executeUrl, new WebhookExecuteRequest
            {
                Content = $"burst {i}",
            });

            if ((int)last.StatusCode == 429)
                got429 = true;
        }

        Assert.True(got429, $"Expected a 429 during the burst; last status was {(int)last.StatusCode}");
        Assert.True(last.Headers.Contains("Retry-After"));
    }
}
