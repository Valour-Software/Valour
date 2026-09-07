using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Valour.Database.Context;
using Valour.Server.Database;
using Valour.Server.Services;
using Valour.Shared.Models;

namespace Valour.Tests.Apis;

[Collection("ApiCollection")]
public class SentryVoiceRoutingTests(LoginTestFixture fixture)
{
    [Theory]
    [InlineData("token", false)]
    [InlineData("leave", false)]
    [InlineData("token", true)]
    [InlineData("leave", true)]
    public async Task StaleVoiceChannelForDeletedPlanet_ReturnsNotFound(string operation, bool legacy)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
        var planets = scope.ServiceProvider.GetRequiredService<PlanetService>();
        var user = await scope.ServiceProvider.GetRequiredService<UserService>().GetAsync(fixture.Client.Me.Id);
        var created = await planets.CreateAsync(new Valour.Server.Models.Planet { Name = "Sentry voice routing", Description = "Voice routing regression", OwnerId = user.Id }, user);
        Assert.True(created.Success, created.Message);
        var planet = created.Data;
        var channelId = IdManager.Generate();
        try
        {
            db.Channels.Add(new Valour.Database.Channel
            {
                Id = channelId, PlanetId = planet.Id, Name = "Voice", Description = "Stale voice channel", ChannelType = ChannelTypeEnum.PlanetVoice,
                LastUpdateTime = DateTime.UtcNow, Version = ISharedChannel.CurrentVersion
            });
            await db.SaveChangesAsync();
            await db.Planets.Where(x => x.Id == planet.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted, true));
            scope.ServiceProvider.GetRequiredService<HostedPlanetService>().Remove(planet.Id);
            var prefix = legacy ? "api/voice/realtimekit" : "api/voice";
            var path = operation == "token" ? $"{prefix}/token/{channelId}" : $"{prefix}/channels/{channelId}/leave";
            using var response = await fixture.Client.Http.PostAsync(path, null);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            await db.Planets.IgnoreQueryFilters().Where(x => x.Id == planet.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted, false));
            await planets.DeleteAsync(planet.Id);
        }
    }
}
