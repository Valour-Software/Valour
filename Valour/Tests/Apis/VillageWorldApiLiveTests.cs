using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Valour.Database.Context;
using Valour.Shared.Models;
using Valour.Shared.Villages;

namespace Valour.Tests.Apis;

/// <summary>
/// End-to-end coverage that a planet's village is genuinely persisted rather
/// than rebuilt per request: the same map ids must come back on a second load,
/// which is the whole difference between this and the proof of concept it
/// replaced.
/// </summary>
[Collection("ApiCollection")]
public class VillageWorldApiLiveTests : IAsyncLifetime
{
    private readonly LoginTestFixture _fixture;

    // xUnit builds a fresh instance per test method, and the test user has a cap
    // on owned planets, so one planet is created for the class and shared rather
    // than one per test.
    private static readonly SemaphoreSlim PlanetGate = new(1, 1);
    private static Valour.Sdk.Models.Planet? _sharedPlanet;

    private Valour.Sdk.Models.Planet _planet = null!;

    public VillageWorldApiLiveTests(LoginTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync()
    {
        await PlanetGate.WaitAsync();
        try
        {
            if (_sharedPlanet is null)
            {
                var create = await new Valour.Sdk.Models.Planet(_fixture.Client)
                {
                    Name = $"Village E2E {Guid.NewGuid().ToString()[..8]}",
                    Description = "Village world test planet",
                }.CreateAsync();

                Assert.True(create.Success, create.Message);

                _sharedPlanet = await _fixture.Client.PlanetService.FetchPlanetAsync(create.Data.Id, skipCache: true);
            }
        }
        finally
        {
            PlanetGate.Release();
        }

        _planet = _sharedPlanet!;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // Goes through the SDK's own fetch so the tests see exactly what the client
    // sees - including its bypass of the node's short GET cache, which would
    // otherwise satisfy a post-edit reload with the pre-edit world.
    private Task<VillagePocScene?> LoadSceneAsync() =>
        _fixture.Client.VillageService.FetchProofOfConceptSceneAsync(_planet.Id)
            .ContinueWith(t => t.Result.Success ? t.Result.Data : null);

    [Fact]
    public async Task FirstOpen_SeedsAWalkableWorld()
    {
        var scene = await LoadSceneAsync();
        Assert.NotNull(scene);

        // An outdoor square plus an interior per building.
        var outdoor = scene!.Maps.Single(x => x.MapKind == "Outdoor");
        Assert.True(outdoor.Width > 0 && outdoor.Height > 0);
        Assert.Contains(scene.Maps, x => x.MapKind == "Interior");

        Assert.Equal(outdoor.Id, scene.StartingMapId);
        Assert.NotNull(outdoor.SpawnTile);

        // The starter world has somewhere to go and something to claim.
        Assert.NotEmpty(outdoor.Buildings);
        Assert.NotEmpty(outdoor.Plots);
    }

    [Fact]
    public async Task SecondOpen_ReturnsTheSameWorld()
    {
        var first = await LoadSceneAsync();
        var second = await LoadSceneAsync();

        Assert.NotNull(first);
        Assert.NotNull(second);

        // Identical ids are the proof that this is stored rather than fabricated:
        // the previous implementation minted fresh ids on every request.
        Assert.Equal(
            first!.Maps.Select(x => x.Id).OrderBy(x => x),
            second!.Maps.Select(x => x.Id).OrderBy(x => x));

        Assert.Equal(first.StartingMapId, second.StartingMapId);
        Assert.Equal(
            first.Maps.SelectMany(x => x.Buildings).Select(x => x.Id).OrderBy(x => x),
            second.Maps.SelectMany(x => x.Buildings).Select(x => x.Id).OrderBy(x => x));
    }

    [Fact]
    public async Task NodeReconnect_RestoresVillagePresenceAtTheLastTile()
    {
        var scene = await LoadSceneAsync();
        var outdoor = scene!.Maps.Single(x => x.Id == scene.StartingMapId);
        var spawn = outdoor.SpawnTile!;

        var joined = await _fixture.Client.VillageService.JoinMapAsync(
            _planet,
            outdoor.Id,
            spawn.X,
            spawn.Y);
        Assert.True(joined.Success, joined.Message);

        using var scope = _fixture.Factory.Services.CreateScope();
        var presenceService = scope.ServiceProvider
            .GetRequiredService<Valour.Server.Services.Villages.VillagePresenceService>();
        var userId = _planet.MyMember!.UserId;

        // Model the server-side effect of a dead SignalR connection, then fire
        // the same client lifecycle event Node raises after authentication is
        // restored.
        await presenceService.LeaveAllForUserAsync(userId);
        Assert.Empty(presenceService.GetMapOccupants(_planet.Id, outdoor.Id));

        _fixture.Client.NodeService.NodeReconnected?.Invoke(_planet.Node);

        VillagePresence? restored = null;
        for (var attempt = 0; attempt < 40 && restored is null; attempt++)
        {
            restored = presenceService
                .GetMapOccupants(_planet.Id, outdoor.Id)
                .SingleOrDefault(x => x.UserId == userId);
            if (restored is null)
                await Task.Delay(50);
        }

        Assert.NotNull(restored);
        Assert.Equal(spawn.X, restored.X);
        Assert.Equal(spawn.Y, restored.Y);

        await _fixture.Client.VillageService.LeaveMapAsync();
    }

    [Fact]
    public async Task RelistingProperty_CreatesANewSaleIdentity()
    {
        var scene = await LoadSceneAsync();
        var plot = scene!.Maps.SelectMany(x => x.Plots).First();
        var originalForSale = plot.ForSale;
        var originalPrice = plot.Price;

        Assert.True((await _fixture.Client.VillageService.SetPlotListingAsync(
            _planet, plot.Id, false, plot.Price)).Success);
        Assert.True((await _fixture.Client.VillageService.SetPlotListingAsync(
            _planet, plot.Id, true, plot.Price)).Success);

        string? firstSaleId;
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
            firstSaleId = await db.VillagePlots
                .AsNoTracking()
                .Where(x => x.Id == plot.Id)
                .Select(x => x.SaleId)
                .SingleAsync();
        }

        Assert.False(string.IsNullOrWhiteSpace(firstSaleId));

        Assert.True((await _fixture.Client.VillageService.SetPlotListingAsync(
            _planet, plot.Id, false, plot.Price)).Success);
        Assert.True((await _fixture.Client.VillageService.SetPlotListingAsync(
            _planet, plot.Id, true, plot.Price)).Success);

        string? secondSaleId;
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
            secondSaleId = await db.VillagePlots
                .AsNoTracking()
                .Where(x => x.Id == plot.Id)
                .Select(x => x.SaleId)
                .SingleAsync();
        }

        Assert.False(string.IsNullOrWhiteSpace(secondSaleId));
        Assert.NotEqual(firstSaleId, secondSaleId);

        var restore = await _fixture.Client.VillageService.SetPlotListingAsync(
            _planet, plot.Id, originalForSale, originalPrice);
        Assert.True(restore.Success, restore.Message);
    }

    [Fact]
    public async Task SpawnAndDoors_AreWalkable()
    {
        var scene = await LoadSceneAsync();
        var outdoor = scene!.Maps.Single(x => x.MapKind == "Outdoor");

        var blocked = new HashSet<(int, int)>();

        foreach (var building in outdoor.Buildings)
        {
            foreach (var rect in building.CollisionRects)
            {
                for (var y = rect.Y; y < rect.Y + rect.Height; y++)
                {
                    for (var x = rect.X; x < rect.X + rect.Width; x++)
                        blocked.Add((x, y));
                }
            }

            foreach (var entrance in building.EntranceTiles)
                blocked.Remove((entrance.X, entrance.Y));
        }

        foreach (var decoration in outdoor.Decorations.Where(x => x.BlocksMovement))
        {
            for (var y = decoration.Y; y < decoration.Y + decoration.Height; y++)
            {
                for (var x = decoration.X; x < decoration.X + decoration.Width; x++)
                    blocked.Add((x, y));
            }
        }

        // Spawning inside a wall would leave the member stuck on arrival.
        Assert.DoesNotContain((outdoor.SpawnTile!.X, outdoor.SpawnTile.Y), blocked);

        // Every door must be steppable, or its building can never be entered.
        foreach (var building in outdoor.Buildings)
        {
            Assert.NotEmpty(building.EntranceTiles);
            foreach (var entrance in building.EntranceTiles)
                Assert.DoesNotContain((entrance.X, entrance.Y), blocked);
        }
    }

    [Fact]
    public async Task AuthoredObjectFootprints_MatchTheirGroundContact()
    {
        var scene = await LoadSceneAsync();
        var outdoor = scene!.Maps.Single(x => x.MapKind == "Outdoor");

        var smallTrees = outdoor.Decorations
            .Where(x => x.DefinitionKey is "small-tree" or "small-tree.with-grass"
                or "small-tree-planter" or "small-tree-planter.square")
            .ToList();
        Assert.NotEmpty(smallTrees);
        Assert.All(smallTrees, tree =>
        {
            Assert.Equal(2, tree.Width);
            Assert.Equal(1, tree.Height);
        });

        var largeTrees = outdoor.Decorations
            .Where(x => x.DefinitionKey is "trees.large-tree" or "trees.large-tree.with-grass"
                or "trees.large-tree-planter" or "large-tree-planter.square")
            .ToList();
        Assert.NotEmpty(largeTrees);
        Assert.All(largeTrees, tree =>
        {
            Assert.Equal(3, tree.Width);
            Assert.Equal(1, tree.Height);
        });

        var fountain = Assert.Single(outdoor.Decorations, x => x.DefinitionKey == "decor.stone-fountain");
        Assert.Equal(2, fountain.Width);
        Assert.Equal(2, fountain.Height);

        var marketStall = Assert.Single(outdoor.Decorations, x => x.DefinitionKey == "commerce.market-stall");
        Assert.Equal(2, marketStall.Width);
        Assert.Equal(1, marketStall.Height);
    }

    [Fact]
    public async Task UnlinkedBuilding_GetsTemporaryVideoRoomAndChat()
    {
        var scene = await LoadSceneAsync();
        var building = scene!.Maps
            .SelectMany(x => x.Buildings)
            .First(x => x.ChannelId is null);
        var interior = scene.Maps.Single(x => x.Id == building.InteriorMapId);

        Assert.Equal(VillageVoiceMode.AutoRoom, building.VoiceMode);

        var joined = await _fixture.Client.VillageService.JoinMapAsync(
            _planet,
            interior.Id,
            interior.SpawnTile!.X,
            interior.SpawnTile.Y,
            building.Id);
        Assert.True(joined.Success, joined.Message);

        var acquired = await _fixture.Client.VillageService.AcquireBuildingRoomAsync(_planet, building.Id);
        Assert.True(acquired.Success, acquired.Message);
        Assert.NotNull(acquired.Data);
        Assert.True(acquired.Data.SupportsVideo);

        var callChannel = await _planet.FetchChannelAsync(acquired.Data.ChannelId);
        Assert.NotNull(callChannel);
        Assert.Equal(ChannelTypeEnum.PlanetVideo, callChannel.ChannelType);
        Assert.True(ISharedChannel.IsVillageEphemeral(callChannel));
        Assert.Equal(acquired.Data.ChatChannelId, callChannel.AssociatedChatChannelId);

        var chatChannel = await _planet.FetchChannelAsync(acquired.Data.ChatChannelId);
        Assert.NotNull(chatChannel);
        Assert.Equal(ChannelTypeEnum.PlanetChat, chatChannel.ChannelType);
        Assert.True(ISharedChannel.IsVillageEphemeral(chatChannel));

        var released = await _fixture.Client.VillageService.ReleaseBuildingRoomAsync(_planet, building.Id);
        Assert.True(released.Success, released.Message);
        await _fixture.Client.VillageService.LeaveMapAsync();
    }

    [Fact]
    public async Task EveryInterior_HasAWayBackOut()
    {
        var scene = await LoadSceneAsync();

        foreach (var interior in scene!.Maps.Where(x => x.MapKind == "Interior"))
        {
            Assert.NotNull(interior.ParentBuildingId);

            // Without an exit portal the member is trapped inside.
            var exit = Assert.Single(interior.Portals);
            Assert.NotNull(exit.TargetMapId);
            Assert.Equal(scene.StartingMapId, exit.TargetMapId);
        }
    }

    [Fact]
    public async Task EveryBuildingDoor_LeadsToARealInteriorWithoutAnOutdoorPortal()
    {
        var scene = await LoadSceneAsync();
        var mapIds = scene!.Maps.Select(x => x.Id).ToHashSet();

        foreach (var map in scene.Maps)
        {
            foreach (var portal in map.Portals)
            {
                Assert.NotNull(portal.TargetMapId);
                Assert.Contains(portal.TargetMapId!.Value, mapIds);
            }

            foreach (var building in map.Buildings)
            {
                Assert.NotEmpty(building.EntranceTiles);
                Assert.NotNull(building.InteriorMapId);
                Assert.Contains(building.InteriorMapId!.Value, mapIds);
                Assert.DoesNotContain(map.Portals, portal => portal.BuildingId == building.Id);
            }
        }
    }

    [Fact]
    public async Task BuildingsAreWiredToChannels()
    {
        var scene = await LoadSceneAsync();
        var outdoor = scene!.Maps.Single(x => x.MapKind == "Outdoor");

        // The starter world binds its civic buildings to whatever channels the
        // planet already has, which is what makes them useful on day one.
        Assert.Contains(outdoor.Buildings, x => x.ChannelId is not null);
    }

    [Fact]
    public async Task Scene_TellsThePlanetOwnerTheyCanManage()
    {
        var scene = await LoadSceneAsync();

        // The owner holds every planet permission, so the scene must offer the
        // management UI; the flag is what the client keys all of it off.
        Assert.True(scene!.CanManageVillage);
    }

    [Fact]
    public async Task Scene_ProvidesAnInGameBuildCatalogAndEditableScopes()
    {
        var scene = await LoadSceneAsync();

        Assert.NotNull(scene);
        Assert.False(string.IsNullOrWhiteSpace(scene!.BuildCatalogImageUrl));
        Assert.Contains(scene.BuildCatalog, x => x.Kind == "Tile");
        Assert.Contains(scene.BuildCatalog, x => x.Kind == "Sprite" && x.Key == "furniture.park-bench");
        Assert.Contains(scene.BuildCatalog, x =>
            x.Kind == "Sprite" &&
            x.Key == "buildings.apartment-small-brown" &&
            x.Category == "Buildings" &&
            x.FootprintWidth == 6 &&
            x.FootprintHeight == 4);
        Assert.Contains(scene.BuildTerrains, x => x.Key == "dirt-path" && x.Name == "Dirt Path");
        Assert.Contains(scene.BuildBrushes, x =>
            x.Key == "brush.tall-grass.5x5" && x.Name == "Tall Grass" && x.Cells.Count == 25);

        // This fixture owns the planet and therefore holds ManageVillage. A
        // regular property owner receives individual CanEdit plot bounds
        // outdoors and a whole-map grant only for their owned interiors.
        Assert.All(scene.Maps, map => Assert.True(map.CanEdit));
    }

    [Fact]
    public async Task BuildMode_PaintsFurnishesAndErasesPersistently()
    {
        var scene = await LoadSceneAsync();
        var outdoor = scene!.Maps.Single(x => x.MapKind == "Outdoor");
        var plot = outdoor.Plots.First(x => x.Name == "Founder's Grove");
        var paintX = plot.X + 1;
        var paintY = plot.Y + 1;
        var furnitureX = plot.X + 1;
        var furnitureY = plot.Y + 3;
        var createdIds = new List<long>();

        try
        {
            var outsideMap = await _fixture.Client.VillageService.EditMapAsync(
                _planet,
                outdoor.Id,
                new VillageBuildRequest
                {
                    Action = VillageBuildAction.Furnish,
                    DefinitionKey = "furniture.park-bench",
                    X = int.MaxValue,
                    Y = furnitureY,
                });
            Assert.False(outsideMap.Success);

            var painted = await _fixture.Client.VillageService.EditMapAsync(
                _planet,
                outdoor.Id,
                new VillageBuildRequest
                {
                    Action = VillageBuildAction.Paint,
                    TerrainKey = "grass-dark",
                    X = paintX,
                    Y = paintY,
                });
            Assert.True(painted.Success, painted.Message);
            Assert.NotNull(painted.Data.Decoration);
            createdIds.Add(painted.Data.Decoration.Id);

            var furnished = await _fixture.Client.VillageService.EditMapAsync(
                _planet,
                outdoor.Id,
                new VillageBuildRequest
                {
                    Action = VillageBuildAction.Furnish,
                    DefinitionKey = "furniture.park-bench",
                    X = furnitureX,
                    Y = furnitureY,
                });
            Assert.True(furnished.Success, furnished.Message);
            Assert.NotNull(furnished.Data.Decoration);
            Assert.True(furnished.Data.Decoration.BlocksMovement);
            createdIds.Add(furnished.Data.Decoration.Id);

            var persisted = await LoadSceneAsync();
            var persistedMap = persisted!.Maps.Single(x => x.Id == outdoor.Id);
            Assert.Contains(persistedMap.GroundTiles, x => x.Id == painted.Data.Decoration.Id);
            Assert.Contains(persistedMap.Decorations, x => x.Id == furnished.Data.Decoration.Id);

            foreach (var objectId in createdIds.ToArray())
            {
                var erased = await _fixture.Client.VillageService.EditMapAsync(
                    _planet,
                    outdoor.Id,
                    new VillageBuildRequest
                    {
                        Action = VillageBuildAction.Erase,
                        ObjectId = objectId,
                    });
                Assert.True(erased.Success, erased.Message);
                createdIds.Remove(objectId);
            }

            var afterErase = await LoadSceneAsync();
            Assert.DoesNotContain(
                afterErase!.Maps.Single(x => x.Id == outdoor.Id).GroundTiles,
                x => x.Id == painted.Data.Decoration.Id);
            Assert.DoesNotContain(
                afterErase.Maps.Single(x => x.Id == outdoor.Id).Decorations,
                x => x.Id == furnished.Data.Decoration.Id);
        }
        finally
        {
            // Keep the shared planet clean if an assertion after placement
            // fails; later tests should never inherit editor furniture.
            foreach (var objectId in createdIds)
            {
                await _fixture.Client.VillageService.EditMapAsync(
                    _planet,
                    outdoor.Id,
                    new VillageBuildRequest
                    {
                        Action = VillageBuildAction.Erase,
                        ObjectId = objectId,
                    });
            }
        }
    }

    [Fact]
    public async Task BuildMode_DoorSpriteCreatesAndArchivesRealInterior()
    {
        var scene = await LoadSceneAsync();
        var outdoor = scene!.Maps.Single(x => x.MapKind == "Outdoor");
        var plot = outdoor.Plots.First(x => x.Name == "Founder's Grove");
        long? buildingId = null;
        long? interiorId = null;
        long? interiorObjectId = null;

        try
        {
            var result = await _fixture.Client.VillageService.EditMapAsync(
                _planet,
                outdoor.Id,
                new VillageBuildRequest
                {
                    Action = VillageBuildAction.Furnish,
                    DefinitionKey = "buildings.apartment-small-brown",
                    X = plot.X,
                    Y = plot.Y,
                });
            Assert.True(result.Success, result.Message);
            Assert.True(result.Data.SceneChanged);
            buildingId = Assert.IsType<long>(result.Data.BuildingId);
            interiorId = Assert.IsType<long>(result.Data.InteriorMapId);
            Assert.Null(result.Data.Decoration);

            var persistedScene = await LoadSceneAsync();
            var persistedOutdoor = persistedScene!.Maps.Single(x => x.Id == outdoor.Id);
            var building = Assert.Single(persistedOutdoor.Buildings, item => item.Id == buildingId);
            Assert.Equal("buildings.apartment-small-brown", building.SpriteKey);
            Assert.Equal(6, building.Width);
            Assert.Equal(4, building.Height);
            Assert.Equal(2, building.EntranceTiles.Count);
            Assert.DoesNotContain(persistedOutdoor.Portals, portal => portal.BuildingId == buildingId);

            var interior = persistedScene.Maps.Single(x => x.Id == interiorId);
            Assert.Equal(buildingId, interior.ParentBuildingId);
            var exit = Assert.Single(interior.Portals);
            Assert.Equal(outdoor.Id, exit.TargetMapId);
            Assert.Equal(building.EntranceTile!.X, exit.TargetX);
            Assert.Equal(building.EntranceTile.Y, exit.TargetY);

            var furnishing = await _fixture.Client.VillageService.EditMapAsync(
                _planet,
                interior.Id,
                new VillageBuildRequest
                {
                    Action = VillageBuildAction.Furnish,
                    DefinitionKey = "furniture.park-bench",
                    X = 2,
                    Y = 2,
                });
            Assert.True(furnishing.Success, furnishing.Message);
            interiorObjectId = furnishing.Data.Decoration!.Id;

            var archive = await _fixture.Client.VillageService.EditMapAsync(
                _planet,
                outdoor.Id,
                new VillageBuildRequest
                {
                    Action = VillageBuildAction.Erase,
                    ObjectId = buildingId,
                });
            Assert.True(archive.Success, archive.Message);
            Assert.True(archive.Data.SceneChanged);

            var afterArchive = await LoadSceneAsync();
            Assert.DoesNotContain(afterArchive!.Maps.SelectMany(map => map.Buildings), item => item.Id == buildingId);
            Assert.DoesNotContain(afterArchive.Maps, map => map.Id == interiorId);

            using var scope = _fixture.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
            var archivedBuilding = await db.VillageBuildings
                .IgnoreQueryFilters()
                .SingleAsync(item => item.Id == buildingId);
            var archivedInterior = await db.VillageMaps
                .IgnoreQueryFilters()
                .SingleAsync(item => item.Id == interiorId);
            Assert.NotNull(archivedBuilding.ArchivedAt);
            Assert.Equal(archivedBuilding.ArchivedAt, archivedInterior.ArchivedAt);
            Assert.True(await db.VillageObjects.AnyAsync(item =>
                item.Id == interiorObjectId && item.MapId == interiorId));

            buildingId = null;
        }
        finally
        {
            if (buildingId is not null)
            {
                await _fixture.Client.VillageService.EditMapAsync(
                    _planet,
                    outdoor.Id,
                    new VillageBuildRequest
                    {
                        Action = VillageBuildAction.Erase,
                        ObjectId = buildingId,
                    });
            }
        }
    }

    [Fact]
    public async Task TerrainBrush_ResolvesNeighborCornersAtomically()
    {
        var scene = await LoadSceneAsync();
        var outdoor = scene!.Maps.Single(x => x.MapKind == "Outdoor");
        var plot = outdoor.Plots.First(x => x.Name == "Founder's Grove");
        var originX = plot.X + 2;
        var originY = plot.Y + 2;
        var cells = new[]
        {
            (X: originX, Y: originY),
            (X: originX + 1, Y: originY),
            (X: originX, Y: originY + 1),
            (X: originX + 1, Y: originY + 1),
        };
        var createdIds = new List<long>();

        try
        {
            var result = await _fixture.Client.VillageService.EditMapAsync(
                _planet,
                outdoor.Id,
                new VillageBuildRequest
                {
                    Action = VillageBuildAction.Paint,
                    TerrainKey = "dirt-path",
                    Cells = cells.Select(cell => new VillageBuildCell
                    {
                        X = cell.X,
                        Y = cell.Y,
                    }).ToList(),
                });
            Assert.True(result.Success, result.Message);
            Assert.NotNull(result.Data.Decoration);
            Assert.Equal(4, result.Data.Decorations.Count);
            createdIds.AddRange(result.Data.Decorations.Select(item => item.Id));

            Assert.Contains(result.Data.Decorations, item =>
                item.DefinitionKey == "grass.dirt-path-flat-grass-path-nw");
            Assert.Contains(result.Data.Decorations, item =>
                item.DefinitionKey == "grass.dirt-path-flat-grass-path-ne");
            Assert.Contains(result.Data.Decorations, item =>
                item.DefinitionKey == "grass.dirt-path-flat-grass-path-sw");
            Assert.Contains(result.Data.Decorations, item =>
                item.DefinitionKey == "grass.dirt-path-flat-grass-path-se");

            var persisted = (await LoadSceneAsync())!.Maps.Single(x => x.Id == outdoor.Id);
            var painted = persisted.GroundTiles
                .Where(item => cells.Contains((item.X, item.Y)))
                .ToDictionary(item => (item.X, item.Y));
            Assert.Equal("grass.dirt-path-flat-grass-path-nw", painted[(originX, originY)].DefinitionKey);
            Assert.Equal("grass.dirt-path-flat-grass-path-ne", painted[(originX + 1, originY)].DefinitionKey);
            Assert.Equal("grass.dirt-path-flat-grass-path-sw", painted[(originX, originY + 1)].DefinitionKey);
            Assert.Equal("grass.dirt-path-flat-grass-path-se", painted[(originX + 1, originY + 1)].DefinitionKey);
        }
        finally
        {
            foreach (var objectId in createdIds.Distinct())
            {
                await _fixture.Client.VillageService.EditMapAsync(
                    _planet,
                    outdoor.Id,
                    new VillageBuildRequest
                    {
                        Action = VillageBuildAction.Erase,
                        ObjectId = objectId,
                    });
            }
        }
    }

    [Fact]
    public async Task ManualBrush_PersistsItsExactAuthoredPattern()
    {
        var scene = await LoadSceneAsync();
        var outdoor = scene!.Maps.Single(x => x.MapKind == "Outdoor");
        var plot = outdoor.Plots.First(x => x.Name == "Founder's Grove");
        var centerX = plot.X + plot.Width / 2;
        var centerY = plot.Y + plot.Height / 2;
        var createdIds = new List<long>();

        try
        {
            var result = await _fixture.Client.VillageService.EditMapAsync(
                _planet,
                outdoor.Id,
                new VillageBuildRequest
                {
                    Action = VillageBuildAction.Paint,
                    BrushKey = "brush.path-in-grass.3x3",
                    Cells = [new VillageBuildCell { X = centerX, Y = centerY }],
                });
            Assert.True(result.Success, result.Message);
            var manualTiles = result.Data.Decorations.Where(item => item.ZIndex == -101).ToList();
            Assert.Equal(9, manualTiles.Count);
            Assert.Equal(9, manualTiles.Select(item => (item.X, item.Y)).Distinct().Count());
            Assert.Contains(manualTiles, item => item.DefinitionKey == "grass.dirt-path-flat");
            createdIds.AddRange(manualTiles.Select(item => item.Id));

            var persisted = (await LoadSceneAsync())!.Maps.Single(x => x.Id == outdoor.Id);
            Assert.Equal(9, persisted.GroundTiles.Count(item =>
                item.ZIndex == -101 &&
                item.X >= centerX - 1 && item.X <= centerX + 1 &&
                item.Y >= centerY - 1 && item.Y <= centerY + 1));
        }
        finally
        {
            foreach (var objectId in createdIds.Distinct())
            {
                await _fixture.Client.VillageService.EditMapAsync(
                    _planet,
                    outdoor.Id,
                    new VillageBuildRequest
                    {
                        Action = VillageBuildAction.Erase,
                        ObjectId = objectId,
                    });
            }
        }
    }

    [Fact]
    public async Task PropertyOwner_CanEditTheirPlotAndBuildingInteriorWithoutManagerPermission()
    {
        var scene = await LoadSceneAsync();
        var outdoor = scene!.Maps.Single(x => x.MapKind == "Outdoor");
        var plot = outdoor.Plots.First(x => x.Name == "Founder's Grove");
        var building = outdoor.Buildings.First();
        var interior = scene.Maps.Single(x => x.Id == building.InteriorMapId);
        var memberId = _planet.MyMember!.Id;
        var createdIds = new List<(long MapId, long ObjectId)>();

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
        var worldService = scope.ServiceProvider
            .GetRequiredService<Valour.Server.Services.Villages.VillageWorldService>();
        var storedPlot = await db.VillagePlots.SingleAsync(x => x.Id == plot.Id);
        var storedBuilding = await db.VillageBuildings.SingleAsync(x => x.Id == building.Id);
        var originalPlotOwner = storedPlot.OwnerMemberId;
        var originalBuildingOwner = storedBuilding.OwnerMemberId;

        try
        {
            storedPlot.OwnerMemberId = memberId;
            storedBuilding.OwnerMemberId = memberId;
            await db.SaveChangesAsync();

            var rejectedManualBrush = await worldService.EditMapAsync(
                _planet.Id, outdoor.Id, memberId, canManageVillage: false,
                new VillageBuildRequest
                {
                    Action = VillageBuildAction.Paint,
                    BrushKey = "brush.path-in-grass.3x3",
                    // The center is owned, but the authored 3x3 footprint
                    // crosses the parcel boundary and must fail atomically.
                    Cells = [new VillageBuildCell { X = plot.X, Y = plot.Y }],
                });
            Assert.False(rejectedManualBrush.Success);

            var insideCell = (X: plot.X + 1, Y: plot.Y + 1);
            var editablePlots = await db.VillagePlots
                .Where(item => item.PlanetId == _planet.Id && item.MapId == outdoor.Id &&
                               (item.EditMode == VillageEditMode.Everyone ||
                                (item.EditMode == VillageEditMode.Owner && item.OwnerMemberId == memberId)))
                .ToListAsync();
            var outsideCell = (
                from y in Enumerable.Range(0, outdoor.Height)
                from x in Enumerable.Range(0, outdoor.Width)
                where !editablePlots.Any(item =>
                    x >= item.X && y >= item.Y &&
                    (long)x < (long)item.X + item.Width &&
                    (long)y < (long)item.Y + item.Height)
                select (X: x, Y: y)).First();
            var beforeRejectedStroke = await db.VillageObjects
                .AsNoTracking()
                .Where(item => item.PlanetId == _planet.Id && item.MapId == outdoor.Id &&
                               ((item.X == insideCell.X && item.Y == insideCell.Y) ||
                                (item.X == outsideCell.X && item.Y == outsideCell.Y)))
                .Select(item => new { item.Id, item.DefinitionKey, item.X, item.Y })
                .OrderBy(item => item.Id)
                .ToListAsync();

            var rejectedStroke = await worldService.EditMapAsync(
                _planet.Id, outdoor.Id, memberId, canManageVillage: false,
                new VillageBuildRequest
                {
                    Action = VillageBuildAction.Paint,
                    TerrainKey = "grass-dark",
                    Cells =
                    [
                        new VillageBuildCell { X = insideCell.X, Y = insideCell.Y },
                        new VillageBuildCell { X = outsideCell.X, Y = outsideCell.Y },
                    ],
                });
            Assert.False(rejectedStroke.Success);
            var afterRejectedStroke = await db.VillageObjects
                .AsNoTracking()
                .Where(item => item.PlanetId == _planet.Id && item.MapId == outdoor.Id &&
                               ((item.X == insideCell.X && item.Y == insideCell.Y) ||
                                (item.X == outsideCell.X && item.Y == outsideCell.Y)))
                .Select(item => new { item.Id, item.DefinitionKey, item.X, item.Y })
                .OrderBy(item => item.Id)
                .ToListAsync();
            Assert.Equal(beforeRejectedStroke, afterRejectedStroke);

            var outdoors = await worldService.EditMapAsync(
                _planet.Id, outdoor.Id, memberId, canManageVillage: false,
                new VillageBuildRequest
                {
                    Action = VillageBuildAction.Paint,
                    TerrainKey = "grass-dark",
                    X = insideCell.X,
                    Y = insideCell.Y,
                });
            Assert.True(outdoors.Success, outdoors.Message);
            createdIds.Add((outdoor.Id, outdoors.Data.Decoration!.Id));

            var indoors = await worldService.EditMapAsync(
                _planet.Id, interior.Id, memberId, canManageVillage: false,
                new VillageBuildRequest
                {
                    Action = VillageBuildAction.Furnish,
                    DefinitionKey = "furniture.park-bench",
                    X = 2,
                    Y = 2,
                });
            Assert.True(indoors.Success, indoors.Message);
            createdIds.Add((interior.Id, indoors.Data.Decoration!.Id));
        }
        finally
        {
            foreach (var (mapId, objectId) in createdIds)
            {
                await worldService.EditMapAsync(
                    _planet.Id, mapId, memberId, canManageVillage: false,
                    new VillageBuildRequest
                    {
                        Action = VillageBuildAction.Erase,
                        ObjectId = objectId,
                    });
            }

            storedPlot.OwnerMemberId = originalPlotOwner;
            storedBuilding.OwnerMemberId = originalBuildingOwner;
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Manager_CanRenameAndRedescribeABuilding()
    {
        var scene = await LoadSceneAsync();
        var building = scene!.Maps.Single(x => x.MapKind == "Outdoor").Buildings.First();

        var update = await _fixture.Client.VillageService.UpdateBuildingAsync(
            _planet, building.Id, "Renamed Hall", "A new description.");
        Assert.True(update.Success, update.Message);

        var updated = (await LoadSceneAsync())!.Maps
            .SelectMany(x => x.Buildings)
            .Single(x => x.Id == building.Id);
        Assert.Equal("Renamed Hall", updated.Name);
        Assert.Equal("A new description.", updated.Hint);

        // The planet is shared by every test in this class, so put the world
        // back the way it was found.
        var restore = await _fixture.Client.VillageService.UpdateBuildingAsync(
            _planet, building.Id, building.Name, building.Hint);
        Assert.True(restore.Success, restore.Message);
    }

    [Fact]
    public async Task Manager_CanRelinkAndUnlinkABuildingChannel()
    {
        var scene = await LoadSceneAsync();
        Assert.NotNull(scene!.DefaultChatChannelId);

        // Use a building that starts unlinked so this test restores the world
        // to exactly the state the other tests expect.
        var building = scene.Maps
            .SelectMany(x => x.Buildings)
            .First(x => x.ChannelId is null);
        var interior = scene.Maps.Single(x => x.Id == building.InteriorMapId);

        var joined = await _fixture.Client.VillageService.JoinMapAsync(
            _planet,
            interior.Id,
            interior.SpawnTile!.X,
            interior.SpawnTile.Y,
            building.Id);
        Assert.True(joined.Success, joined.Message);

        var temporaryRoom = await _fixture.Client.VillageService
            .AcquireBuildingRoomAsync(_planet, building.Id);
        Assert.True(temporaryRoom.Success, temporaryRoom.Message);
        Assert.NotNull(temporaryRoom.Data);

        var link = await _fixture.Client.VillageService.UpdateBuildingAsync(
            _planet, building.Id, name: null, description: null,
            updateChannel: true, channelId: scene.DefaultChatChannelId);
        Assert.True(link.Success, link.Message);

        var linked = (await LoadSceneAsync())!.Maps
            .SelectMany(x => x.Buildings)
            .Single(x => x.Id == building.Id);
        Assert.Equal(scene.DefaultChatChannelId, linked.ChannelId);

        // A chat building surfaces the same channel for nearby text.
        Assert.Equal(scene.DefaultChatChannelId, linked.ChatChannelId);

        // Rebinding retires the temporary channel immediately instead of
        // leaving a stale private room alive beside the permanent binding.
        Assert.Null(await _planet.FetchChannelAsync(
            temporaryRoom.Data.ChannelId,
            skipCache: true));

        await _fixture.Client.VillageService.LeaveMapAsync();

        var unlink = await _fixture.Client.VillageService.UpdateBuildingAsync(
            _planet, building.Id, name: null, description: null,
            updateChannel: true, channelId: null);
        Assert.True(unlink.Success, unlink.Message);

        var unlinked = (await LoadSceneAsync())!.Maps
            .SelectMany(x => x.Buildings)
            .Single(x => x.Id == building.Id);
        Assert.Null(unlinked.ChannelId);

        // Unlinked buildings fall back to leased private area rooms.
        Assert.Equal(VillageVoiceMode.AutoRoom, unlinked.VoiceMode);
    }

    [Fact]
    public async Task Update_RejectsChannelsTheBuildingCannotSurface()
    {
        var scene = await LoadSceneAsync();
        var building = scene!.Maps.Single(x => x.MapKind == "Outdoor").Buildings.First();

        // A channel id from another planet (or nowhere) must not bind.
        var foreign = await _fixture.Client.VillageService.UpdateBuildingAsync(
            _planet, building.Id, name: null, description: null,
            updateChannel: true, channelId: long.MaxValue);
        Assert.False(foreign.Success);

        // Categories organise the sidebar; walking into one makes no sense.
        await _planet.FetchChannelsAsync();
        var category = _planet.Channels.First(x => x.ChannelType == ChannelTypeEnum.PlanetCategory);
        var wrongType = await _fixture.Client.VillageService.UpdateBuildingAsync(
            _planet, building.Id, name: null, description: null,
            updateChannel: true, channelId: category.Id);
        Assert.False(wrongType.Success);

        // Neither failed attempt may have moved the binding.
        var after = (await LoadSceneAsync())!.Maps
            .SelectMany(x => x.Buildings)
            .Single(x => x.Id == building.Id);
        Assert.Equal(building.ChannelId, after.ChannelId);
    }

    [Fact]
    public async Task Update_RejectsUnusableNames()
    {
        var scene = await LoadSceneAsync();
        var building = scene!.Maps.Single(x => x.MapKind == "Outdoor").Buildings.First();

        var blank = await _fixture.Client.VillageService.UpdateBuildingAsync(
            _planet, building.Id, "   ", description: null);
        Assert.False(blank.Success);

        var tooLong = await _fixture.Client.VillageService.UpdateBuildingAsync(
            _planet, building.Id, new string('x', 49), description: null);
        Assert.False(tooLong.Success);

        var after = (await LoadSceneAsync())!.Maps
            .SelectMany(x => x.Buildings)
            .Single(x => x.Id == building.Id);
        Assert.Equal(building.Name, after.Name);
    }

    [Fact]
    public async Task Plot_CanBeRenamed()
    {
        var scene = await LoadSceneAsync();
        var plot = scene!.Maps.Single(x => x.MapKind == "Outdoor").Plots.First();

        var update = await _fixture.Client.VillageService.UpdatePlotAsync(_planet, plot.Id, "Sunset Field");
        Assert.True(update.Success, update.Message);

        var renamed = (await LoadSceneAsync())!.Maps
            .SelectMany(x => x.Plots)
            .Single(x => x.Id == plot.Id);
        Assert.Equal("Sunset Field", renamed.Name);

        var restore = await _fixture.Client.VillageService.UpdatePlotAsync(_planet, plot.Id, plot.Name);
        Assert.True(restore.Success, restore.Message);
    }

    [Fact]
    public async Task Stranger_CannotEditProperty()
    {
        var scene = await LoadSceneAsync();
        var outdoor = scene!.Maps.Single(x => x.MapKind == "Outdoor");
        var building = outdoor.Buildings.First();
        var plot = outdoor.Plots.First();

        // The fixture only carries one HTTP user, so the authorization rule is
        // exercised against the real service with a member id that owns nothing
        // and no manage permission.
        using var scope = _fixture.Factory.Services.CreateScope();
        var worldService = scope.ServiceProvider
            .GetRequiredService<Valour.Server.Services.Villages.VillageWorldService>();

        var buildingResult = await worldService.UpdateBuildingAsync(
            building.Id, _planet.Id, actorMemberId: -1, canManageVillage: false,
            new VillageBuildingUpdateRequest { Name = "Hijacked" });
        Assert.False(buildingResult.Success);

        var plotResult = await worldService.UpdatePlotAsync(
            plot.Id, _planet.Id, actorMemberId: -1, canManageVillage: false,
            new VillagePlotUpdateRequest { Name = "Hijacked" });
        Assert.False(plotResult.Success);

        var buildResult = await worldService.EditMapAsync(
            _planet.Id,
            outdoor.Id,
            actorMemberId: -1,
            canManageVillage: false,
            new VillageBuildRequest
            {
                Action = VillageBuildAction.Furnish,
                DefinitionKey = "furniture.park-bench",
                X = plot.X,
                Y = plot.Y,
            });
        Assert.False(buildResult.Success);

        var after = await LoadSceneAsync();
        Assert.Equal(building.Name, after!.Maps.SelectMany(x => x.Buildings).Single(x => x.Id == building.Id).Name);
        Assert.Equal(plot.Name, after.Maps.SelectMany(x => x.Plots).Single(x => x.Id == plot.Id).Name);
    }
}
