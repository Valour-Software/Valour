using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Valour.Database.Context;
using Valour.Sdk.Models.Embeds;
using Valour.Shared.Models;

namespace Valour.Tests.Apis;

/// <summary>
/// Not a regular test: seeds the shared test database with a user, planet,
/// and embed message for manual browser verification, then writes the
/// credentials to the path in the BROWSER_SEED_OUT env var. Skipped unless
/// that variable is set.
/// </summary>
[Collection("ApiCollection")]
public class BrowserSeedTest
{
    private readonly LoginTestFixture _fixture;

    public BrowserSeedTest(LoginTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SeedEmbedDataForBrowser()
    {
        var outPath = Environment.GetEnvironmentVariable("BROWSER_SEED_OUT");
        Assert.SkipWhen(string.IsNullOrEmpty(outPath), "BROWSER_SEED_OUT not set; seed skipped.");

        var client = _fixture.Client;

        var create = await new Valour.Sdk.Models.Planet(client)
        {
            Name = "Embed Browser Test",
            Description = "Browser verification planet",
        }.CreateAsync();
        Assert.True(create.Success, create.Message);

        var planet = await client.PlanetService.FetchPlanetAsync(create.Data.Id, skipCache: true);
        var channel = await planet.FetchPrimaryChatChannelAsync();

        var embed = new EmbedBuilder()
            .WithEmbedId("browser-embed")
            .WithRevision(1)
            .AddPage("Embed Engine v2", "rendered by the new engine")
                .AddText("Status", "Waiting for update...").WithId("status-text")
                .AddForm("browser-form")
                    .AddInputBox("name-input", name: "Your Name", placeholder: "type here...")
                    .AddButton("Submit").WithId("submit-btn").OnClickSubmitForm("form-submitted")
                .EndForm()
                .AddProgress("Progress")
                    .AddProgressBar(25).WithId("bar-1").WithLabel().Striped()
                .AddRow()
                    .AddButton("Go to page 2").OnClickPage(1)
                    .AddButton("Valour site").OnClickLink("https://valour.gg")
                .EndRow()
            .AddPage("Page Two", "second page footer")
                .AddText("You made it to page two.")
            .Build();

        var sendResult = await channel.SendMessageAsync("Embed engine browser test", embed: embed);
        Assert.True(sendResult.Success, sendResult.Message);
        var messageId = sendResult.Data.Id;

        // The message worker stages messages before persisting; the standalone
        // server has its own worker, so wait until this one is in the database
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ValourDb>();

        var persisted = false;
        for (var i = 0; i < 60 && !persisted; i++)
        {
            persisted = await db.Messages.AnyAsync(x => x.Id == messageId);
            if (!persisted)
                await Task.Delay(500);
        }
        Assert.True(persisted, "Seeded message was not persisted in time.");

        // Mark the user as a bot so it can push live embed updates
        var user = await db.Users.FirstAsync(x => x.Id == client.Me.Id);
        user.Bot = true;
        await db.SaveChangesAsync();

        var seed = new
        {
            Token = client.AuthService.Token,
            UserId = client.Me.Id,
            PlanetId = planet.Id,
            PlanetName = planet.Name,
            ChannelId = channel.Id,
            MessageId = messageId,
        };

        await File.WriteAllTextAsync(outPath, JsonSerializer.Serialize(seed, new JsonSerializerOptions { WriteIndented = true }));
    }
}
