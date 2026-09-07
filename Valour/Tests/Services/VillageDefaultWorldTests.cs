using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Valour.Database;
using Valour.Database.Context;
using Valour.Database.Migrations;
using Valour.Database.Seeds.Villages;
using Valour.Server.Services;
using Valour.Server.Services.Villages;
using Valour.Shared.Models;

namespace Valour.Tests.Services;

[Collection("ApiCollection")]
public class VillageDefaultWorldTests(LoginTestFixture fixture)
{
    [Fact]
    public void BundledWorld_IsPortableAndEveryDoorAndArrivalIsReachable()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<VillageTemplateService>();
        var world = JsonSerializer.Deserialize<VillageTemplateSnapshot>(VillageDefaultWorld.CurrentJson)!;
        Assert.Null(service.Validate(world));
        Assert.Equal(5, world.Maps.Count);
        Assert.Equal(4, world.Buildings.Count);
        Assert.Equal(5, world.Plots.Count);
        Assert.Equal(2343, world.Objects.Count);
        Assert.All(world.Maps, map =>
        {
            Assert.Equal(0, map.PlanetId);
            Assert.Null(map.Planet);
            Assert.Null(map.ArchivedAt);
            Assert.Contains(world.Objects, x => x.MapId == map.Id && x.ZIndex <= -100);
        });
        Assert.All(world.Buildings, x => { Assert.Equal(0, x.PlanetId); Assert.Null(x.OwnerMemberId); Assert.Null(x.SaleId); });
        Assert.All(world.Plots, x => { Assert.Equal(0, x.PlanetId); Assert.Null(x.OwnerMemberId); Assert.Null(x.SaleId); });
        Assert.All(world.Objects, x => { Assert.Equal(0, x.PlanetId); Assert.Null(x.OwnerMemberId); });
        var ids = world.Maps.Select(x => x.Id).Concat(world.Buildings.Select(x => x.Id))
            .Concat(world.Plots.Select(x => x.Id)).Concat(world.Objects.Select(x => x.Id)).Concat(world.Chunks.Select(x => x.Id)).ToList();
        Assert.Equal(Enumerable.Range(1, ids.Count).Select(x => (long)x), ids.Order());
        Assert.Equal(world.Buildings.Where(x => x.ChannelId.HasValue).Select(x => x.ChannelId!.Value).Distinct().Order(), world.ChannelTypes.Keys.Order());
        Assert.All(world.ChannelTypes.Values, type => Assert.Contains(type, new[] { ChannelTypeEnum.PlanetChat, ChannelTypeEnum.PlanetVoice, ChannelTypeEnum.PlanetVideo }));
    }

    [Fact]
    public async Task Migration_FreshInstallIsRepeatableAndRollbackOnlyClearsTheSeed()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
        Assert.Contains("20260907120000_SeedVillageDefaultWorld", db.Database.GetMigrations());
        Assert.False(db.Database.HasPendingModelChanges());
        await using var transaction = await db.Database.BeginTransactionAsync();
        await CreateTemporarySettingsAsync(db);
        await ApplyAsync(db, up: true);
        await ApplyAsync(db, up: true);
        var installed = await db.VillageTemplates.AsNoTracking().SingleAsync();
        Assert.Equal(VillageDefaultWorld.Version1Json, installed.PublishedJson);
        Assert.Equal(1, installed.Revision);
        Assert.Equal(1, installed.ResetBeforeRevision);
        Assert.Null(installed.DraftPlanetId);
        var status = await scope.ServiceProvider.GetRequiredService<VillageTemplateService>().GetStatusAsync();
        Assert.True(status.IsBuiltIn);
        Assert.Equal(5, status.PublishedMapCount);
        await ApplyAsync(db, up: false);
        var reverted = await db.VillageTemplates.AsNoTracking().SingleAsync();
        Assert.Null(reverted.PublishedJson);
        Assert.Equal(1, reverted.Revision);
        await ApplyAsync(db, up: true);
        Assert.Equal(VillageDefaultWorld.Version1Json, await db.VillageTemplates.Select(x => x.PublishedJson).SingleAsync());
    }

    [Fact]
    public async Task Migration_SeedsAnUnpublishedDraftWithoutChangingItsRevisionOrResetThreshold()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await CreateTemporarySettingsAsync(db);
        db.VillageTemplates.Add(new() { Id = 1, Revision = 4, ResetBeforeRevision = 2, DraftPlanetId = 123 });
        await db.SaveChangesAsync();
        await ApplyAsync(db, up: true);
        var installed = await db.VillageTemplates.AsNoTracking().SingleAsync();
        Assert.Equal(VillageDefaultWorld.Version1Json, installed.PublishedJson);
        Assert.Equal(4, installed.Revision);
        Assert.Equal(2, installed.ResetBeforeRevision);
        Assert.Equal(123, installed.DraftPlanetId);
        await ApplyAsync(db, up: false);
        var reverted = await db.VillageTemplates.AsNoTracking().SingleAsync();
        Assert.Null(reverted.PublishedJson);
        Assert.Equal(123, reverted.DraftPlanetId);
        Assert.Equal(4, reverted.Revision);
        Assert.Equal(2, reverted.ResetBeforeRevision);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Migration_PreservesStaffPublicationsDuringUpgradeRollbackAndReapply(bool sameLayout)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await CreateTemporarySettingsAsync(db);
        var original = new VillageTemplate
        {
            Id = 1, Revision = 12, ResetBeforeRevision = 8, DraftPlanetId = 123,
            PublishedJson = sameLayout ? VillageDefaultWorld.Version1Json : "{\"Maps\":[]}",
            PublishedAt = new DateTime(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc), PublishedByUserId = 456
        };
        db.VillageTemplates.Add(original);
        await db.SaveChangesAsync();
        foreach (var up in new[] { true, false, true })
        {
            await ApplyAsync(db, up);
            var actual = await db.VillageTemplates.AsNoTracking().SingleAsync();
            Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(actual));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BundledWorld_ClonesCompleteLayoutsWithIndependentIdsAndDestinationChannels(bool installed)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
        var templates = scope.ServiceProvider.GetRequiredService<VillageTemplateService>();
        var planets = scope.ServiceProvider.GetRequiredService<PlanetService>();
        var seed = VillageDefaultWorld.CurrentJson;
        var original = JsonSerializer.Deserialize<VillageTemplateSnapshot>(seed)!;
        var created = new List<Valour.Sdk.Models.Planet>();
        var seenIds = new HashSet<long>();
        var saleIds = new HashSet<string>();
        try
        {
            for (var pass = 0; pass < 2; pass++)
            {
                var result = await new Valour.Sdk.Models.Planet(fixture.Client)
                {
                    Name = $"Seed copy {Guid.NewGuid().ToString()[..8]}", Description = "Bundled world regression", EnableVillage = true
                }.CreateAsync();
                Assert.True(result.Success, result.Message);
                var planet = (await fixture.Client.PlanetService.FetchPlanetAsync(result.Data.Id, skipCache: true))!;
                created.Add(planet);
                await templates.CopyPublishedAsync(planet.Id, await planets.GetAllChannelsAsync(planet.Id),
                    new VillageTemplate { PublishedJson = installed ? seed : null });
                var maps = await db.VillageMaps.AsNoTracking().Where(x => x.PlanetId == planet.Id).ToListAsync();
                var objects = await db.VillageObjects.AsNoTracking().Where(x => x.PlanetId == planet.Id).ToListAsync();
                var buildings = await db.VillageBuildings.AsNoTracking().Where(x => x.PlanetId == planet.Id).ToListAsync();
                var plots = await db.VillagePlots.AsNoTracking().Where(x => x.PlanetId == planet.Id).ToListAsync();
                Assert.Equal(original.Maps.Count, maps.Count);
                Assert.Equal(original.Objects.Count, objects.Count);
                Assert.All(maps, map =>
                {
                    var source = original.Maps.Single(x => x.Name == map.Name);
                    Assert.Equal(1, map.Version);
                    Assert.Equal(1, map.TemplateRevision);
                    Assert.Equal(original.Objects.Where(x => x.MapId == source.Id).Select(Layout).Order(), objects.Where(x => x.MapId == map.Id).Select(Layout).Order());
                    if (source.ParentBuildingId is { } parent)
                        Assert.Equal(original.Buildings.Single(x => x.Id == parent).Name, buildings.Single(x => x.Id == map.ParentBuildingId).Name);
                });
                foreach (var id in maps.Select(x => x.Id).Concat(objects.Select(x => x.Id)).Concat(buildings.Select(x => x.Id)).Concat(plots.Select(x => x.Id)))
                    Assert.True(id > 10000 && seenIds.Add(id), "Each world must receive independent runtime IDs.");
                Assert.All(objects, x => Assert.Null(x.OwnerMemberId));
                Assert.All(buildings, x =>
                {
                    Assert.Null(x.OwnerMemberId);
                    Assert.Contains(maps, m => m.Id == x.InteriorMapId && m.ParentBuildingId == x.Id);
                    Assert.Contains(plots, p => p.Id == x.PlotId && p.MapId == x.MapId);
                    if (x.ForSale) Assert.True(saleIds.Add(x.SaleId));
                });
                Assert.All(plots, x => { Assert.Null(x.OwnerMemberId); if (x.ForSale) Assert.True(saleIds.Add(x.SaleId)); });
                var chatId = await db.Channels.Where(x => x.PlanetId == planet.Id && x.IsDefault).Select(x => x.Id).SingleAsync();
                Assert.Equal(chatId, buildings.Single(x => x.Name == "Town Hall").ChannelId);
                Assert.Null(templates.Validate(new() { Maps = maps, Objects = objects, Buildings = buildings, Plots = plots }));
            }
            Assert.Equal(seed, VillageDefaultWorld.CurrentJson);
        }
        finally
        {
            foreach (var planet in created) await planet.DeleteAsync();
        }
    }

    private static string Layout(VillageObject item) =>
        $"{item.DefinitionKey}:{item.X},{item.Y}:{item.Rotation}:{item.ZIndex}:{item.BlocksMovement}";

    private static Task CreateTemporarySettingsAsync(ValourDb db) =>
        db.Database.ExecuteSqlRawAsync("CREATE TEMP TABLE village_template (LIKE public.village_template INCLUDING ALL) ON COMMIT DROP");

    private static async Task ApplyAsync(ValourDb db, bool up)
    {
        var migration = new SeedVillageDefaultWorld { ActiveProvider = db.Database.ProviderName };
        var commands = db.GetService<IMigrationsSqlGenerator>().Generate(
            up ? migration.UpOperations : migration.DownOperations, migration.TargetModel);
        foreach (var migrationCommand in commands)
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
            command.CommandText = migrationCommand.CommandText;
            await command.ExecuteNonQueryAsync();
        }
    }
}
