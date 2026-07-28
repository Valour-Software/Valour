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
        Assert.True(collision.IsWalkable(3, 5));
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
}
