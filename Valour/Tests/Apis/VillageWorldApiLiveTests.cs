using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
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
        foreach (var portal in outdoor.Portals)
            Assert.DoesNotContain((portal.X, portal.Y), blocked);
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
    public async Task EveryDoor_LeadsToARealInterior()
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

        var after = await LoadSceneAsync();
        Assert.Equal(building.Name, after!.Maps.SelectMany(x => x.Buildings).Single(x => x.Id == building.Id).Name);
        Assert.Equal(plot.Name, after.Maps.SelectMany(x => x.Plots).Single(x => x.Id == plot.Id).Name);
    }
}
