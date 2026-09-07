using Microsoft.Extensions.DependencyInjection;
using Valour.Server.Services.Villages;
using Valour.Shared.Villages;

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
    public void EveryCatalogBuilding_HasAReachableAuthoredDoor()
    {
        var service = Resolve();
        foreach (var definition in service.GetBuildCatalog("exterior-tileset-0").Where(x => x.Key.StartsWith("buildings.")))
        {
            var footprint = service.GetFootprint(definition.Key);
            var doors = service.GetDoorOffsets("exterior-tileset-0", definition.Key, footprint.Width, footprint.Height);
            Assert.NotEmpty(doors);
            var map = service.BuildMapForTesting(Map(), buildings:
            [new() { X = 10, Y = 10, Width = footprint.Width, Height = footprint.Height, SpriteKey = definition.Key }]);
            var visited = new HashSet<(int X, int Y)> { (12, 20) };
            var queue = new Queue<(int X, int Y)>(); queue.Enqueue((12, 20));
            while (queue.TryDequeue(out var point))
                foreach (var next in new[] { (point.X + 1, point.Y), (point.X - 1, point.Y), (point.X, point.Y + 1), (point.X, point.Y - 1) })
                    if (map.IsWalkable(next.Item1, next.Item2) && visited.Add(next)) queue.Enqueue(next);
            Assert.Contains(doors, door => visited.Contains((door.X + 10, door.Y + 10)));
        }
    }

    [Theory]
    [InlineData("buildings.house-medium")]
    [InlineData("buildings.house-medium.brown")]
    [InlineData("buildings.house-medium.blue")]
    public void HousePorch_CanBeWalkedFromStepsToDoor(string key)
    {
        var collision = Resolve().BuildMapForTesting(Map(), buildings:
        [new() { X = 10, Y = 10, Width = 8, Height = 5, SpriteKey = key, DoorX = 12, DoorY = 12 }]);
        for (var y = 15; y >= 11; y--) Assert.True(collision.IsWalkable(12, y), $"Porch blocked at 12,{y}");
        Assert.True(collision.IsWalkable(11, 13));
        Assert.False(collision.IsWalkable(10, 12));
        Assert.False(collision.IsWalkable(14, 14));
        Assert.False(collision.IsWalkable(12, 10));
    }

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
    public void SemanticWallObject_BlocksExactlyItsMapCellWithoutAtlasMetadata()
    {
        var collision = Resolve().BuildMapForTesting(
            Map(8, 8),
            objects:
            [
                new Valour.Database.VillageObject
                {
                    DefinitionKey = VillageWallTopology.MakeDefinitionKey("modern.green", 46),
                    X = 3,
                    Y = 4,
                    BlocksMovement = true,
                },
            ]);

        Assert.False(collision.IsWalkable(3, 4));
        Assert.True(collision.IsWalkable(3, 3));
        Assert.True(collision.IsWalkable(4, 4));
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

        Assert.Equal(11, terrains.Count);
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

    [Fact]
    public void WallCatalog_DistinguishesRoomBuilderArtFromBlobTopology()
    {
        var walls = Resolve().GetBuildWallSets("exterior-tileset-0");
        Assert.Equal(6, walls.Count);
        var wallSet = Assert.Single(walls, wall => wall.Key == "modern.green");

        Assert.Equal("modern.green", wallSet.Key);
        Assert.Equal(8, wallSet.Columns);
        Assert.Equal(7, wallSet.Rows);
        Assert.Equal(VillageWallTopology.FrameCount, wallSet.FrameCount);
        Assert.Equal("RoomBuilder", wallSet.Layout);
        var image = new Uri(new Uri("http://localhost"), wallSet.ImageUrl);
        Assert.EndsWith("library-atlas.vtex.bin", image.AbsolutePath);
        Assert.Matches(@"^\?v=[a-f0-9]{16}$", image.Query);
    }
}
