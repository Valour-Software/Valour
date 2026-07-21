using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Valour.Database.Context;
using Valour.Server.Database;
using Valour.Server.Services;
using Valour.Shared.Queries;
using ServerPlanet = Valour.Server.Models.Planet;
using ServerThread = Valour.Server.Models.PlanetThread;

namespace Valour.Tests.Services;

[Collection("ApiCollection")]
public class ThreadFeedHydrationIntegrationTests
{
    private readonly LoginTestFixture _fixture;

    public ThreadFeedHydrationIntegrationTests(LoginTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GlobalFeed_HydratesCardData_InSingleResponse()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
        var users = scope.ServiceProvider.GetRequiredService<UserService>();
        var planets = scope.ServiceProvider.GetRequiredService<PlanetService>();
        var members = scope.ServiceProvider.GetRequiredService<PlanetMemberService>();
        var threads = scope.ServiceProvider.GetRequiredService<ThreadService>();

        var owner = await users.GetAsync(_fixture.Client.Me.Id);
        var createdPlanet = await planets.CreateAsync(new ServerPlanet
        {
            Name = "Hydrated Feed Test",
            Description = "request-count regression test",
            OwnerId = owner.Id,
            EnableThreads = true
        }, owner);
        Assert.True(createdPlanet.Success, createdPlanet.Message);

        var planet = createdPlanet.Data!;
        var member = await members.GetByUserAsync(owner.Id, planet.Id);
        var createdThread = await threads.CreateThreadAsync(new ServerThread
        {
            PlanetId = planet.Id,
            Title = "One response",
            Content = "No card-level follow-up requests",
            Attachments = []
        }, member);
        Assert.True(createdThread.Success, createdThread.Message);

        var page = await threads.QueryFeedAsync(owner.Id, new QueryRequest
        {
            Take = 20,
            Options = new QueryOptions()
        });
        var hydrated = Assert.Single(page.Items, x => x.Id == createdThread.Data!.Id);

        Assert.Equal(owner.Id, hydrated.AuthorUser?.Id);
        Assert.Equal(member.Id, hydrated.AuthorMember?.Id);
        Assert.NotNull(hydrated.AuthorRole);
        Assert.NotNull(hydrated.Presence);
        Assert.False(hydrated.ViewerHasBoosted);

        await db.PlanetThreads.Where(x => x.Id == hydrated.Id).ExecuteDeleteAsync();
    }
}
