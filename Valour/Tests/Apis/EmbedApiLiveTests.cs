using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Valour.Database.Context;
using Valour.Sdk.Models.Embeds;
using Valour.Sdk.Models.Embeds.Items;
using Valour.Shared.Models;

namespace Valour.Tests.Apis;

/// <summary>
/// End-to-end tests for the embed engine against the real server:
/// message send/validation, interaction relay, and live updates over SignalR.
/// </summary>
[Collection("ApiCollection")]
public class EmbedApiLiveTests : IAsyncLifetime
{
    private readonly LoginTestFixture _fixture;

    private Valour.Sdk.Models.Planet _planet = null!;
    private Valour.Sdk.Models.Channel _channel = null!;

    public EmbedApiLiveTests(LoginTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync()
    {
        // Use a planet owned by the test user so it has embed permissions
        var create = await new Valour.Sdk.Models.Planet(_fixture.Client)
        {
            Name = $"Embed E2E {Guid.NewGuid().ToString()[..8]}",
            Description = "Embed engine E2E test planet",
        }.CreateAsync();
        Assert.True(create.Success, create.Message);

        // Re-fetch through the service so node routing is fully hydrated
        _planet = await _fixture.Client.PlanetService.FetchPlanetAsync(create.Data.Id, skipCache: true);
        _channel = await _planet.FetchPrimaryChatChannelAsync();
        Assert.NotNull(_channel);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static Embed BuildTestEmbed(long revision = 1)
    {
        return new EmbedBuilder()
            .WithEmbedId("e2e-embed")
            .WithRevision(revision)
            .AddPage("E2E Test", "footer")
                .AddText("status", "waiting").WithId("status-text")
                .AddForm("e2e-form")
                    .AddInputBox("name-input", name: "Name", value: "prefilled")
                    .AddButton("Submit").WithId("submit-btn").OnClickSubmitForm("form-submitted")
                .EndForm()
                .AddProgress("Loading")
                    .AddProgressBar(25).WithId("bar-1").WithLabel()
            .AddPage("Second Page")
                .AddText("page two")
            .Build();
    }

    private async Task SetBotFlagAsync(bool bot)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
        var user = await db.Users.FirstAsync(x => x.Id == _fixture.Client.Me.Id);
        user.Bot = bot;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Embed_FullPipeline_SendInteractAndLiveUpdate()
    {
        var client = _fixture.Client;

        // ---- 1. Send a message with an embed and confirm it round-trips ----

        var sendResult = await _channel.SendMessageAsync("embed test", embed: BuildTestEmbed());
        Assert.True(sendResult.Success, sendResult.Message);
        var message = sendResult.Data;
        Assert.NotNull(message);

        var embed = message.EmbedAttachment?.Embed;
        Assert.NotNull(embed);
        Assert.Equal(2, embed.Pages.Count);
        Assert.IsType<EmbedTextItem>(embed.FindItem("status-text"));

        // ---- 2. Interaction: user clicks submit; bot receives server-stamped event ----

        await _channel.ConnectToRealtime();

        var hub = client.PrimaryNode.HubConnection;
        var interactionReceived = new TaskCompletionSource<EmbedInteractionEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var interactionSub = hub.On<EmbedInteractionEvent>("InteractionEvent",
            e => interactionReceived.TrySetResult(e));

        await hub.InvokeAsync("JoinInteractionGroup", _planet.Id);

        var interactResponse = await client.Http.PostAsJsonAsync("api/embed/interact", new EmbedInteractionRequest
        {
            MessageId = message.Id,
            EventType = EmbedInteractionEventType.FormSubmitted,
            ElementId = "form-submitted",
            FormId = "e2e-form",
            FormData = new List<EmbedFormData>
            {
                new() { ElementId = "name-input", Value = "Alice", Type = EmbedItemType.InputBox },
            },
        });
        Assert.True(interactResponse.IsSuccessStatusCode, await interactResponse.Content.ReadAsStringAsync());

        var interaction = await interactionReceived.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Context is stamped by the server from the message, not the client
        Assert.Equal(message.Id, interaction.MessageId);
        Assert.Equal(_channel.Id, interaction.ChannelId);
        Assert.Equal(_planet.Id, interaction.PlanetId);
        Assert.Equal(EmbedInteractionEventType.FormSubmitted, interaction.EventType);
        Assert.Equal("form-submitted", interaction.ElementId);
        Assert.Equal("e2e-form", interaction.FormId);
        var formData = Assert.Single(interaction.FormData!);
        Assert.Equal("Alice", formData.Value);

        var myMember = await _planet.FetchMemberByUserAsync(client.Me.Id);
        Assert.Equal(myMember.Id, interaction.MemberId);
        Assert.Equal(message.AuthorMemberId, interaction.AuthorMemberId);

        // ---- 3. Live updates require the bot flag ----

        var update = new EmbedUpdate
        {
            TargetMessageId = message.Id,
            Revision = 2,
            NewEmbedContent = EmbedParser.Serialize(BuildTestEmbed(revision: 2)),
        };

        var nonBotResponse = await client.Http.PostAsJsonAsync("api/embed/update", update);
        Assert.False(nonBotResponse.IsSuccessStatusCode);

        await SetBotFlagAsync(true);
        try
        {
            // ---- 4. Full-replace channel update arrives over SignalR ----

            var updateReceived = new TaskCompletionSource<EmbedUpdate>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task OnUpdate(EmbedUpdate u)
            {
                updateReceived.TrySetResult(u);
                return Task.CompletedTask;
            }

            client.MessageService.EmbedUpdated += OnUpdate;
            try
            {
                var updateResponse = await client.Http.PostAsJsonAsync("api/embed/update", update);
                Assert.True(updateResponse.IsSuccessStatusCode, await updateResponse.Content.ReadAsStringAsync());

                var received = await updateReceived.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(message.Id, received.TargetMessageId);
                Assert.Equal(_channel.Id, received.TargetChannelId); // stamped server-side
                Assert.Equal(2, received.Revision);
                Assert.Null(received.TargetUserId);

                var receivedEmbed = EmbedParser.TryParse(received.NewEmbedContent);
                Assert.NotNull(receivedEmbed);
                Assert.Equal(2, receivedEmbed.Revision);
            }
            finally
            {
                client.MessageService.EmbedUpdated -= OnUpdate;
            }

            // ---- 5. Targeted item update (now supported channel-wide) ----

            var targetedReceived = new TaskCompletionSource<EmbedUpdate>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task OnTargeted(EmbedUpdate u)
            {
                targetedReceived.TrySetResult(u);
                return Task.CompletedTask;
            }

            client.MessageService.EmbedUpdated += OnTargeted;
            try
            {
                var changedItems = new List<EmbedItem>
                {
                    new EmbedTextItem("done!") { Id = "status-text" },
                    new EmbedProgressBarItem { Id = "bar-1", Value = 100 },
                };

                var targetedUpdate = new EmbedUpdate
                {
                    TargetMessageId = message.Id,
                    Revision = 3,
                    ChangedItemsContent = System.Text.Json.JsonSerializer.Serialize(changedItems),
                };

                var targetedResponse = await client.Http.PostAsJsonAsync("api/embed/update", targetedUpdate);
                Assert.True(targetedResponse.IsSuccessStatusCode, await targetedResponse.Content.ReadAsStringAsync());

                var received = await targetedReceived.Task.WaitAsync(TimeSpan.FromSeconds(10));
                var items = EmbedParser.TryParseItems(received.ChangedItemsContent);
                Assert.NotNull(items);
                Assert.Equal(2, items.Count);

                // Apply to the local model the way the client component does
                foreach (var item in items)
                    Assert.True(embed.ReplaceItem(item));

                Assert.Equal("done!", ((EmbedTextItem)embed.FindItem("status-text")!).Text);
                Assert.Equal(100, ((EmbedProgressBarItem)embed.FindItem("bar-1")!).Value);
            }
            finally
            {
                client.MessageService.EmbedUpdated -= OnTargeted;
            }

            // ---- 6. Unsafe CSS in an update is rejected ----

            var evilItems = new List<EmbedItem>
            {
                new EmbedTextItem("evil") { Id = "status-text", Style = "background-image: url(https://evil.example);" },
            };

            var evilResponse = await client.Http.PostAsJsonAsync("api/embed/update", new EmbedUpdate
            {
                TargetMessageId = message.Id,
                ChangedItemsContent = System.Text.Json.JsonSerializer.Serialize(evilItems),
            });
            Assert.False(evilResponse.IsSuccessStatusCode);
        }
        finally
        {
            await SetBotFlagAsync(false);
        }
    }

    private async Task<Valour.Sdk.Models.Message> CreateBareMessageAsync(string content)
    {
        await _planet.EnsureReadyAsync();

        return new Valour.Sdk.Models.Message(_fixture.Client)
        {
            Content = content,
            ChannelId = _channel.Id,
            PlanetId = _planet.Id,
            AuthorUserId = _fixture.Client.Me.Id,
            AuthorMemberId = _planet.MyMember?.Id,
            Fingerprint = Guid.NewGuid().ToString(),
        };
    }

    [Fact]
    public async Task SendMessage_WithLegacyV1EmbedPayload_IsRejected()
    {
        var message = await CreateBareMessageAsync("legacy embed");
        message.SetEmbedPayload("{\"Pages\":[],\"EmbedVersion\":\"1.3\"}");

        var result = await _fixture.Client.MessageService.SendMessage(message);
        Assert.False(result.Success);
    }

    [Fact]
    public async Task SendMessage_WithUnsafeEmbedStyle_IsRejected()
    {
        var embed = new EmbedBuilder()
            .AddPage("bad").AddText("x")
            .Build();
        embed.Pages[0].Children[0].Style = "width: expression(alert(1));";

        var message = await CreateBareMessageAsync("unsafe embed");
        message.SetEmbed(embed);

        var result = await _fixture.Client.MessageService.SendMessage(message);
        Assert.False(result.Success);
    }
}
