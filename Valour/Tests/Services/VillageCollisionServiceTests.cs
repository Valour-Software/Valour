using Microsoft.Extensions.DependencyInjection;
using Valour.Server.Services.Villages;

namespace Valour.Tests.Services;

[Collection("ApiCollection")]
public class VillageCollisionServiceTests
{
    private readonly LoginTestFixture _fixture;

    public VillageCollisionServiceTests(LoginTestFixture fixture)
    {
        _fixture = fixture;
    }

    private VillageCollisionService Resolve() =>
        _fixture.Factory.Services.GetRequiredService<VillageCollisionService>();

    private static Valour.Database.VillageMap Map(int width = 96, int height = 96) =>
        new()
        {
            Id = 1,
            PlanetId = 1,
            Width = width,
            Height = height,
            TilesetKey = "exterior-tileset-0",
            Name = "Test",
        };

    [Fact]
    public void BuildingFootprint_IsBlockedButAuthoredDoorWins()
    {
        var collision = Resolve().BuildMapForTesting(
            Map(10, 10),
            buildings:
            [
                new Valour.Database.VillageBuilding
                {
                    X = 2,
                    Y = 2,
                    Width = 3,
                    Height = 4,
                    DoorX = 3,
                    DoorY = 3,
                },
            ]);

        Assert.False(collision.IsWalkable(2, 2));
        Assert.False(collision.IsWalkable(4, 4));
        Assert.True(collision.IsWalkable(3, 3));
        Assert.False(collision.IsWalkable(3, 5));
    }

    [Fact]
    public void ObjectCollision_UsesExactBottomAnchoredTilesetMask()
    {
        var collision = Resolve().BuildMapForTesting(
            Map(),
            objects:
            [
                new Valour.Database.VillageObject
                {
                    DefinitionKey = "small-tree",
                    X = 10,
                    Y = 10,
                    BlocksMovement = true,
                },
                new Valour.Database.VillageObject
                {
                    DefinitionKey = "trees.large-tree",
                    X = 20,
                    Y = 20,
                    BlocksMovement = true,
                },
                new Valour.Database.VillageObject
                {
                    DefinitionKey = "decor.stone-fountain",
                    X = 30,
                    Y = 30,
                    BlocksMovement = true,
                },
            ]);

        Assert.False(collision.IsWalkable(10, 10));
        Assert.False(collision.IsWalkable(11, 10));
        Assert.True(collision.IsWalkable(10, 9));

        // The large tree's canopy is three tiles wide, but its persisted
        // collision mask blocks only the trunk in the middle.
        Assert.True(collision.IsWalkable(20, 20));
        Assert.False(collision.IsWalkable(21, 20));
        Assert.True(collision.IsWalkable(22, 20));

        Assert.False(collision.IsWalkable(30, 30));
        Assert.False(collision.IsWalkable(31, 30));
        Assert.False(collision.IsWalkable(30, 31));
        Assert.False(collision.IsWalkable(31, 31));
    }

    [Fact]
    public void BuildingSpriteCollision_UsesItsGroundFootprintNotFacadeHeight()
    {
        var collision = Resolve().BuildMapForTesting(
            Map(),
            objects:
            [
                new Valour.Database.VillageObject
                {
                    DefinitionKey = "buildings.apartment-tall-brown",
                    X = 20,
                    Y = 20,
                    BlocksMovement = true,
                },
            ]);

        Assert.False(collision.IsWalkable(20, 20));
        Assert.False(collision.IsWalkable(26, 24));
        Assert.True(collision.IsWalkable(23, 23));
        Assert.True(collision.IsWalkable(23, 24));
        Assert.True(collision.IsWalkable(20, 19));
        Assert.True(collision.IsWalkable(20, 25));
    }

    [Fact]
    public void ChunkCollision_AcceptsCompactBlockedIndices()
    {
        var collision = Resolve().BuildMapForTesting(
            Map(),
            chunks:
            [
                new Valour.Database.VillageMapChunk
                {
                    Id = 7,
                    ChunkX = 1,
                    ChunkY = 2,
                    CollisionData = """{"blocked":[0,33]}""",
                },
            ]);

        Assert.False(collision.IsWalkable(32, 64));
        Assert.False(collision.IsWalkable(33, 65));
        Assert.True(collision.IsWalkable(34, 65));
    }

    [Fact]
    public void MalformedChunkCollision_BlocksChunkFailClosed()
    {
        var collision = Resolve().BuildMapForTesting(
            Map(),
            chunks:
            [
                new Valour.Database.VillageMapChunk
                {
                    Id = 8,
                    ChunkX = 1,
                    ChunkY = 1,
                    CollisionData = """{"unexpected":true}""",
                },
            ]);

        Assert.False(collision.IsWalkable(32, 32));
        Assert.False(collision.IsWalkable(63, 63));
        Assert.True(collision.IsWalkable(31, 31));
    }

    [Fact]
    public void MapBounds_AreNeverWalkable()
    {
        var collision = Resolve().BuildMapForTesting(Map(4, 3));

        Assert.True(collision.IsWalkable(0, 0));
        Assert.True(collision.IsWalkable(3, 2));
        Assert.False(collision.IsWalkable(-1, 0));
        Assert.False(collision.IsWalkable(0, -1));
        Assert.False(collision.IsWalkable(4, 2));
        Assert.False(collision.IsWalkable(3, 3));
    }

    [Fact]
    public void TerrainResolver_PicksConnectedCornersFromLogicalNeighbors()
    {
        var service = Resolve();
        var terrain = new Dictionary<(int X, int Y), string>
        {
            [(1, 1)] = "dirt-path",
            [(2, 1)] = "dirt-path",
            [(1, 2)] = "dirt-path",
            [(2, 2)] = "dirt-path",
        };
        string TerrainAt(int x, int y) => terrain.GetValueOrDefault((x, y), "grass");

        Assert.True(service.TryResolveTerrainDefinition(
            "exterior-tileset-0", "dirt-path", TerrainAt, 4, 4, 1, 1, out var northWest));
        Assert.True(service.TryResolveTerrainDefinition(
            "exterior-tileset-0", "dirt-path", TerrainAt, 4, 4, 2, 1, out var northEast));
        Assert.True(service.TryResolveTerrainDefinition(
            "exterior-tileset-0", "dirt-path", TerrainAt, 4, 4, 1, 2, out var southWest));
        Assert.True(service.TryResolveTerrainDefinition(
            "exterior-tileset-0", "dirt-path", TerrainAt, 4, 4, 2, 2, out var southEast));

        Assert.Equal("grass.dirt-path-flat-grass-path-nw", northWest.Key);
        Assert.Equal("grass.dirt-path-flat-grass-path-ne", northEast.Key);
        Assert.Equal("grass.dirt-path-flat-grass-path-sw", southWest.Key);
        Assert.Equal("grass.dirt-path-flat-grass-path-se", southEast.Key);
    }

    [Fact]
    public void TerrainCatalog_ContainsOnlyLogicalBrushesWithBasePreviews()
    {
        var terrains = Resolve().GetBuildTerrains("exterior-tileset-0");

        Assert.Equal(4, terrains.Count);
        Assert.Contains(terrains, terrain =>
            terrain.Key == "dirt-path" &&
            terrain.Name == "Dirt Path" &&
            terrain.Preview.TerrainRole == "Base");
        Assert.DoesNotContain(terrains, terrain => terrain.Key.Contains("edge", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ManualBrushCatalog_PreservesAuthoredPatterns()
    {
        var brushes = Resolve().GetBuildBrushes("exterior-tileset-0");

        Assert.Equal(3, brushes.Count);
        var path = Assert.Single(brushes, brush => brush.Key == "brush.path-in-grass.3x3");
        Assert.Equal("Path in Grass", path.Name);
        Assert.Equal(3, path.Size);
        Assert.Equal(9, path.Cells.Count);
        Assert.All(path.Cells, cell => Assert.False(string.IsNullOrWhiteSpace(cell.DefinitionKey)));
    }
}
