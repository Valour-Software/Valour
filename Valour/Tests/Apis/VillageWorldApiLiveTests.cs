using System.Net.Http.Json;
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

    private Task<VillagePocScene?> LoadSceneAsync() =>
        _fixture.Client.PrimaryNode.GetJsonAsync<VillagePocScene>($"api/planets/{_planet.Id}/village/poc")
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
}
