using Valour.Sdk.Client;
using Valour.Sdk.Models;
using Valour.Sdk.Models.Threads;
using Valour.Shared.Models;

namespace Valour.Tests.Services;

public class ThreadFeedHydrationTests
{
    [Fact]
    public void RicherConcurrentFeedResponse_WinsWithoutBeingClearedBySparseResponse()
    {
        var client = new ValourClient("https://hub.example/")
        {
            Me = new User(null) { Id = 7, Name = "Viewer" }
        };
        client.Me.SetClient(client);

        var planet = new Planet(client) { Id = 42, Name = "Test" }.Sync(client);
        var first = new PlanetThread(client)
        {
            Id = 100,
            PlanetId = planet.Id,
            AuthorUserId = 8,
            AuthorUser = new User(client) { Id = 8, Name = "Author" },
            ViewerHasBoosted = false
        }.Sync(client);

        var presence = new PlanetPresenceSummary { ChattingCount = 12, Avatars = [] };
        var richer = new PlanetThread(client)
        {
            Id = first.Id,
            PlanetId = planet.Id,
            AuthorUserId = 8,
            Presence = presence,
            ViewerHasBoosted = true
        }.Sync(client);

        Assert.Same(first, richer);
        Assert.Same(presence, first.Presence);
        Assert.True(first.ViewerHasBoosted);
        Assert.Equal("Author", first.AuthorUser.Name);

        var sparse = new PlanetThread(client)
        {
            Id = first.Id,
            PlanetId = planet.Id,
            AuthorUserId = 8
        }.Sync(client);

        Assert.Same(first, sparse);
        Assert.Same(presence, first.Presence);
        Assert.True(first.ViewerHasBoosted);
        Assert.Equal("Author", first.AuthorUser.Name);
    }

    [Fact]
    public void SyncingViewerMembership_PopulatesPlanetMyMember()
    {
        var client = new ValourClient("https://hub.example/");
        client.Me = new User(client) { Id = 7, Name = "Viewer" };
        var planet = new Planet(client) { Id = 42, Name = "Test" }.Sync(client);

        var member = new PlanetMember(client)
        {
            Id = 99,
            PlanetId = planet.Id,
            UserId = client.Me.Id,
            User = client.Me
        }.Sync(client);

        Assert.Same(member, planet.MyMember);
    }
}
