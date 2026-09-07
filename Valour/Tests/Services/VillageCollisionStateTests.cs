using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using Valour.Client.Components.Windows.Villages;
using Valour.Server.Services.Villages;
using Valour.Shared.Villages;

namespace Valour.Tests.Services;

public class VillageCollisionStateTests
{
    [Fact]
    public void TilesetDraft_LegacyBooleansImportAndNamedStatesExport()
    {
        var draft = JsonSerializer.Deserialize<
            TilesetDefinitionWindowComponent.TilesetDefinitionDraft>(
            """{"Collision":[true,false,"door","future-hazard"]}""");

        Assert.NotNull(draft);
        Assert.Equal(
            ["solid", "empty", "door", "future-hazard"],
            draft.Collision);

        using var exported = JsonDocument.Parse(JsonSerializer.Serialize(draft));
        Assert.Equal(
            ["solid", "empty", "door", "future-hazard"],
            exported.RootElement.GetProperty("Collision")
                .EnumerateArray()
                .Select(x => x.GetString())
                .ToArray());
    }

    [Fact]
    public void BuildingSpriteCollision_AuthoredDoorCarvesGroundFootprint()
    {
        const int width = 6;
        const int height = 7;
        var states = Enumerable.Repeat(VillageCollisionState.Solid, width * height).ToArray();
        states[(height - 1) * width + 2] = VillageCollisionState.Door;
        var definition = new VillageCollisionService.CollisionDefinition(
            "buildings.apartment-small-brown",
            "Apartment Small Brown",
            "Sprite",
            0,
            0,
            width,
            height,
            states,
            string.Empty,
            "Base",
            "None",
            string.Empty,
            1);
        var map = new Valour.Database.VillageMap
        {
            Id = 1,
            PlanetId = 1,
            Width = 96,
            Height = 96,
            TilesetKey = "exterior-tileset-0",
            Name = "Test",
        };
        var collision = VillageCollisionService.VillageCollisionMap.Build(
            map,
            [
                new Valour.Database.VillageObject
                {
                    DefinitionKey = definition.Key,
                    X = 20,
                    Y = 20,
                    BlocksMovement = true,
                },
            ],
            [],
            [],
            new Dictionary<string, VillageCollisionService.CollisionDefinition>
            {
                [definition.Key] = definition,
            },
            NullLogger.Instance);

        Assert.False(collision.IsWalkable(20, 23));
        Assert.True(collision.IsWalkable(22, 23));
        Assert.False(collision.IsWalkable(25, 23));
        Assert.True(collision.IsWalkable(22, 19));
    }

    [Theory]
    [InlineData(null, "empty")]
    [InlineData("false", "empty")]
    [InlineData("true", "solid")]
    [InlineData("blocked", "solid")]
    [InlineData(" Door ", "door")]
    [InlineData("future-hazard", "future-hazard")]
    public void Normalize_PreservesExtensibleNamedStates(string? input, string expected)
    {
        Assert.Equal(expected, VillageCollisionState.Normalize(input));
    }

    [Theory]
    [InlineData("empty", false)]
    [InlineData("door", false)]
    [InlineData("solid", true)]
    [InlineData("future-hazard", true)]
    public void BlocksMovement_IsPassableOnlyForRegisteredPassableStates(
        string state,
        bool expected)
    {
        Assert.Equal(expected, VillageCollisionState.BlocksMovement(state));
    }
}
