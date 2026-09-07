using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Valour.Database.Context;
using Valour.Server.Services.Villages;
using Valour.Shared.Villages;

namespace Valour.Tests.Services;

[Collection("ApiCollection")]
public class VillageTemplateServiceTests(LoginTestFixture fixture)
{
    [Fact]
    public async Task OrdinaryUser_CannotReadOrPublishStaffTemplate()
    {
        var read = await fixture.Client.PrimaryNode.GetJsonAsync<VillageTemplateStatus>("api/staff/village-template", cacheDurationMs: null);
        Assert.False(read.Success);
        var publish = await fixture.Client.PrimaryNode.PostAsyncWithResponse<VillageTemplateStatus>("api/staff/village-template/publish", new VillageTemplatePublishRequest());
        Assert.False(publish.Success);
    }

    [Fact]
    public async Task Publication_IsFrozen_ClonesLinksAndOwnership_AndResetReplacesOnlyVillage()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
        var templates = scope.ServiceProvider.GetRequiredService<VillageTemplateService>();
        var old = await db.VillageTemplates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1);
        var created = new List<Valour.Sdk.Models.Planet>();
        try
        {
            await db.VillageTemplates.ExecuteDeleteAsync();
            async Task<Valour.Sdk.Models.Planet> Create(string suffix)
            {
                var result = await new Valour.Sdk.Models.Planet(fixture.Client)
                {
                    Name = $"Template {suffix} {Guid.NewGuid().ToString()[..6]}", Description = "Local template regression", EnableVillage = true
                }.CreateAsync();
                Assert.True(result.Success, result.Message);
                var planet = await fixture.Client.PlanetService.FetchPlanetAsync(result.Data.Id, skipCache: true);
                created.Add(planet!); return planet!;
            }
            var source = await Create("draft");
            var sourceScene = await fixture.Client.VillageService.FetchProofOfConceptSceneAsync(source.Id);
            Assert.True(sourceScene.Success, sourceScene.Message);
            var staffId = await db.Planets.Where(x => x.Id == source.Id).Select(x => x.OwnerId).SingleAsync();
            var sourceMember = await db.PlanetMembers.Where(x => x.PlanetId == source.Id && x.UserId == staffId).Select(x => x.Id).SingleAsync();
            await db.VillageBuildings.Where(x => x.PlanetId == source.Id).ExecuteUpdateAsync(x => x.SetProperty(b => b.OwnerMemberId, sourceMember));
            var draft = await templates.SelectDraftAsync(new() { PlanetId = source.Id, ExpectedRevision = 1 }, staffId);
            Assert.True(draft.Success, draft.Message);
            var publish = await templates.PublishAsync(new() { DraftPlanetId = source.Id, ExpectedRevision = 1 }, staffId);
            Assert.True(publish.Success, publish.Message);
            Assert.Equal(2, publish.Data.Revision);
            var stale = await templates.PublishAsync(new() { DraftPlanetId = source.Id, ExpectedRevision = 1 }, staffId);
            Assert.False(stale.Success);
            var original = await db.VillageObjects.AsNoTracking().FirstAsync(x => x.PlanetId == source.Id && x.DefinitionKey == "decor.indoor-plant");
            await db.VillageObjects.Where(x => x.Id == original.Id).ExecuteUpdateAsync(x => x.SetProperty(o => o.X, original.X - 1));
            var target = await Create("copy");
            var copy = await fixture.Client.VillageService.FetchProofOfConceptSceneAsync(target.Id);
            Assert.True(copy.Success, copy.Message);
            Assert.Equal(sourceScene.Data.Maps.Count, copy.Data.Maps.Count);
            Assert.Empty(sourceScene.Data.Maps.Select(x => x.Id).Intersect(copy.Data.Maps.Select(x => x.Id)));
            Assert.All(copy.Data.Maps.SelectMany(x => x.Buildings), x => Assert.Null(x.OwnerMemberId));
            var originalMap = sourceScene.Data.Maps.Single(x => x.Id == original.MapId);
            var copiedMap = copy.Data.Maps.Single(x => x.Name == originalMap.Name);
            Assert.Contains(copiedMap.Decorations, x => x.DefinitionKey == original.DefinitionKey && x.X == original.X && x.Y == original.Y);
            var sourceChannels = await db.Channels.Where(x => x.PlanetId == source.Id).Select(x => x.Id).ToListAsync();
            Assert.DoesNotContain(copy.Data.Maps.SelectMany(x => x.Buildings), x => x.ChannelId is { } id && sourceChannels.Contains(id));
            Assert.All(await db.VillageMaps.AsNoTracking().Where(x => x.PlanetId == target.Id).ToListAsync(), x => Assert.Equal(2, x.TemplateRevision));
            var planets = scope.ServiceProvider.GetRequiredService<Valour.Server.Services.PlanetService>();
            var worlds = scope.ServiceProvider.GetRequiredService<VillageWorldService>();
            await worlds.ResetToTemplateAsync(await planets.GetAsync(target.Id), await planets.GetAllChannelsAsync(target.Id), staffId);
            var reset = await fixture.Client.VillageService.FetchProofOfConceptSceneAsync(target.Id);
            Assert.True(reset.Success, reset.Message);
            Assert.Empty(copy.Data.Maps.Select(x => x.Id).Intersect(reset.Data.Maps.Select(x => x.Id)));
            Assert.True(await db.Planets.AnyAsync(x => x.Id == target.Id));
            Assert.NotEmpty(await db.Channels.Where(x => x.PlanetId == target.Id).ToListAsync());

            var replaceExisting = await templates.PublishAsync(new()
            {
                DraftPlanetId = source.Id, ExpectedRevision = 2, ResetExisting = true
            }, staffId);
            Assert.True(replaceExisting.Success, replaceExisting.Message);
            Assert.Equal(3, replaceExisting.Data.ResetBeforeRevision);
            var replaced = await fixture.Client.VillageService.FetchProofOfConceptSceneAsync(target.Id);
            Assert.True(replaced.Success, replaced.Message);
            Assert.Empty(reset.Data.Maps.Select(x => x.Id).Intersect(replaced.Data.Maps.Select(x => x.Id)));
            Assert.All(await db.VillageMaps.AsNoTracking().Where(x => x.PlanetId == target.Id).ToListAsync(), x => Assert.Equal(3, x.TemplateRevision));
            var reopened = await fixture.Client.VillageService.FetchProofOfConceptSceneAsync(target.Id);
            Assert.Equal(replaced.Data.Maps.Select(x => x.Id).Order(), reopened.Data.Maps.Select(x => x.Id).Order());
            var draftAfterReset = await fixture.Client.VillageService.FetchProofOfConceptSceneAsync(source.Id);
            Assert.True(draftAfterReset.Success, draftAfterReset.Message);
            Assert.Equal(sourceScene.Data.Maps.Select(x => x.Id).Order(), draftAfterReset.Data.Maps.Select(x => x.Id).Order());
        }
        finally
        {
            db.ChangeTracker.Clear();
            await db.VillageTemplates.ExecuteDeleteAsync();
            if (old is not null) { db.VillageTemplates.Add(old); await db.SaveChangesAsync(); }
            foreach (var planet in created) await planet.DeleteAsync();
        }
    }

    [Fact]
    public void Publication_RejectsBlockedArrivalAndDisconnectedInteriors()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<VillageTemplateService>();
        var snapshot = new VillageTemplateSnapshot
        {
            Maps = [new() { Id = 1, Name = "Commons", Width = 12, Height = 12, TileSize = 32, TilesetKey = "exterior-tileset-0", SpawnX = 5, SpawnY = 5 }],
            Objects = [new() { Id = 2, MapId = 1, DefinitionKey = "living.bookcase", X = 5, Y = 5, BlocksMovement = true }]
        };
        Assert.Contains("arrival", service.Validate(snapshot), StringComparison.OrdinalIgnoreCase);
        snapshot.Objects.Clear();
        Assert.Null(service.Validate(snapshot));
        snapshot.Maps.Add(new() { Id = 3, MapType = Valour.Shared.Models.VillageMapType.Interior, Name = "Orphan", Width = 12, Height = 12, TileSize = 32, TilesetKey = "exterior-tileset-0" });
        Assert.Contains("connected building", service.Validate(snapshot));
    }
}
