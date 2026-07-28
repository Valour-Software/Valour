using Microsoft.Extensions.DependencyInjection;
using Valour.Server.Services.Villages;
using Valour.Shared.Villages;

namespace Valour.Tests.Services;

/// <summary>
/// Exercises village presence against the real service resolved from the
/// running server, rather than a stub: CoreHubService has too many collaborators
/// to fake usefully, and the broadcasts it makes into empty hub groups are
/// harmless here.
///
/// Presence is process-wide static state, so every test uses its own planet and
/// user ids to stay independent of the others.
/// </summary>
[Collection("ApiCollection")]
public class VillagePresenceServiceTests
{
    private readonly LoginTestFixture _fixture;

    public VillagePresenceServiceTests(LoginTestFixture fixture)
    {
        _fixture = fixture;
    }

    private IServiceScope CreateScope() => _fixture.Factory.Services.CreateScope();

    private static VillagePresenceService Resolve(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<VillagePresenceService>();

    private static void PrepareMap(
        IServiceScope scope,
        long planetId,
        long mapId,
        long? parentBuildingId = null,
        IEnumerable<(int X, int Y)>? blocked = null) =>
        scope.ServiceProvider
            .GetRequiredService<VillageCollisionService>()
            .SetMapForTesting(planetId, mapId, parentBuildingId: parentBuildingId, blocked: blocked);

    [Fact]
    public async Task JoinMap_PlacesMemberAndReturnsOccupancy()
    {
        using var scope = CreateScope();
        var service = Resolve(scope);

        const long planetId = 900_001;
        const long mapId = 1;
        PrepareMap(scope, planetId, mapId);

        var snapshot = await service.JoinMapAsync(planetId, mapId, userId: 1, memberId: 11, "Ada", "avatar-a", 4, 5);

        Assert.NotNull(snapshot);
        Assert.Equal(planetId, snapshot.PlanetId);
        Assert.Equal(mapId, snapshot.MapId);

        var ada = Assert.Single(snapshot.Presences);
        Assert.Equal(1, ada.UserId);
        Assert.Equal(4, ada.X);
        Assert.Equal(5, ada.Y);
        Assert.Equal("Ada", ada.Name);

        await service.LeaveAllForUserAsync(1);
    }

    [Fact]
    public async Task JoinMap_SecondMemberSeesTheFirst()
    {
        using var scope = CreateScope();
        var service = Resolve(scope);

        const long planetId = 900_002;
        const long mapId = 1;
        PrepareMap(scope, planetId, mapId);

        await service.JoinMapAsync(planetId, mapId, userId: 1, memberId: 11, "Ada", "a", 1, 1);
        var snapshot = await service.JoinMapAsync(planetId, mapId, userId: 2, memberId: 22, "Grace", "g", 2, 2);

        Assert.Equal(2, snapshot.Presences.Count);
        Assert.Contains(snapshot.Presences, x => x.UserId == 1);
        Assert.Contains(snapshot.Presences, x => x.UserId == 2);

        await service.LeaveAllForUserAsync(1);
        await service.LeaveAllForUserAsync(2);
    }

    [Fact]
    public async Task Occupancy_IsScopedToTheMap()
    {
        using var scope = CreateScope();
        var service = Resolve(scope);

        const long planetId = 900_003;
        PrepareMap(scope, planetId, 1);
        PrepareMap(scope, planetId, 2);

        await service.JoinMapAsync(planetId, 1, userId: 1, memberId: 11, "Ada", "a", 0, 0);
        await service.JoinMapAsync(planetId, 2, userId: 2, memberId: 22, "Grace", "g", 0, 0);

        Assert.Single(service.GetMapOccupants(planetId, 1));
        Assert.Single(service.GetMapOccupants(planetId, 2));
        Assert.Equal(1, service.GetMapOccupants(planetId, 1)[0].UserId);

        await service.LeaveAllForUserAsync(1);
        await service.LeaveAllForUserAsync(2);
    }

    [Fact]
    public async Task JoiningAnotherMap_LeavesThePreviousOne()
    {
        using var scope = CreateScope();
        var service = Resolve(scope);

        const long planetId = 900_004;
        PrepareMap(scope, planetId, 1);
        PrepareMap(scope, planetId, 2);

        await service.JoinMapAsync(planetId, 1, userId: 1, memberId: 11, "Ada", "a", 0, 0);
        await service.JoinMapAsync(planetId, 2, userId: 1, memberId: 11, "Ada", "a", 3, 3);

        // Walking through a door must not leave a copy standing outside.
        Assert.Empty(service.GetMapOccupants(planetId, 1));
        Assert.Single(service.GetMapOccupants(planetId, 2));

        await service.LeaveAllForUserAsync(1);
    }

    [Fact]
    public async Task Move_UpdatesPositionAndFacingWithoutTrustingBuildingId()
    {
        using var scope = CreateScope();
        var service = Resolve(scope);

        const long planetId = 900_005;
        const long mapId = 1;
        PrepareMap(scope, planetId, mapId);

        await service.JoinMapAsync(planetId, mapId, userId: 1, memberId: 11, "Ada", "a", 0, 0);

        var moved = service.Move(planetId, mapId, 1, 1, 0, VillageFacing.Left, buildingId: 42);
        Assert.True(moved);

        var presence = Assert.Single(service.GetMapOccupants(planetId, mapId));
        Assert.Equal(1, presence.X);
        Assert.Equal(0, presence.Y);
        Assert.Equal(VillageFacing.Left, presence.Facing);
        Assert.Null(presence.BuildingId);

        await service.LeaveAllForUserAsync(1);
    }

    [Fact]
    public async Task Move_IsThrottled()
    {
        using var scope = CreateScope();
        var service = Resolve(scope);

        const long planetId = 900_006;
        const long mapId = 1;
        PrepareMap(scope, planetId, mapId);

        await service.JoinMapAsync(planetId, mapId, userId: 1, memberId: 11, "Ada", "a", 0, 0);

        Assert.True(service.Move(planetId, mapId, 1, 1, 0, VillageFacing.Right, null));

        // Immediately again: faster than any real client can walk.
        Assert.False(service.Move(planetId, mapId, 1, 2, 0, VillageFacing.Right, null));

        // The rejected move must not have been applied.
        var presence = Assert.Single(service.GetMapOccupants(planetId, mapId));
        Assert.Equal(1, presence.X);

        await service.LeaveAllForUserAsync(1);
    }

    [Fact]
    public async Task Move_IsRejectedForSomeoneNotOnTheMap()
    {
        using var scope = CreateScope();
        var service = Resolve(scope);

        const long planetId = 900_007;
        PrepareMap(scope, planetId, 1);
        PrepareMap(scope, planetId, 2);

        Assert.False(service.Move(planetId, 1, userId: 99, 1, 1, VillageFacing.Down, null));

        await service.JoinMapAsync(planetId, 1, userId: 1, memberId: 11, "Ada", "a", 0, 0);

        // Right user, wrong map.
        Assert.False(service.Move(planetId, 2, userId: 1, 1, 1, VillageFacing.Down, null));

        await service.LeaveAllForUserAsync(1);
    }

    [Fact]
    public async Task Move_RejectsSameMapTeleport()
    {
        using var scope = CreateScope();
        var service = Resolve(scope);

        const long planetId = 900_011;
        const long mapId = 1;
        PrepareMap(scope, planetId, mapId);

        await service.JoinMapAsync(planetId, mapId, userId: 1, memberId: 11, "Ada", "a", 2, 2);

        Assert.False(service.Move(planetId, mapId, 1, 8, 9, VillageFacing.Down, null));

        var presence = Assert.Single(service.GetMapOccupants(planetId, mapId));
        Assert.Equal(2, presence.X);
        Assert.Equal(2, presence.Y);

        await service.LeaveAllForUserAsync(1);
    }

    [Fact]
    public async Task BuildingOccupants_TracksEveryoneOnTheInteriorMap()
    {
        using var scope = CreateScope();
        var service = Resolve(scope);

        const long planetId = 900_008;
        const long mapId = 1;
        const long buildingId = 7;
        PrepareMap(scope, planetId, mapId, parentBuildingId: buildingId);

        await service.JoinMapAsync(planetId, mapId, userId: 1, memberId: 11, "Ada", "a", 0, 0);
        await service.JoinMapAsync(planetId, mapId, userId: 2, memberId: 22, "Grace", "g", 5, 5);

        var occupants = service.GetBuildingOccupants(planetId, buildingId);
        Assert.Equal(2, occupants.Count);
        Assert.Contains(occupants, x => x.UserId == 1);
        Assert.Contains(occupants, x => x.UserId == 2);

        await service.LeaveAllForUserAsync(1);
        await service.LeaveAllForUserAsync(2);
    }

    [Fact]
    public async Task JoinMap_PreservesInteriorBuildingContextImmediately()
    {
        using var scope = CreateScope();
        var service = Resolve(scope);

        const long planetId = 900_012;
        const long mapId = 2;
        const long buildingId = 77;
        PrepareMap(scope, planetId, mapId, parentBuildingId: buildingId);

        await service.JoinMapAsync(
            planetId,
            mapId,
            userId: 1,
            memberId: 11,
            "Ada",
            "a",
            9,
            11,
            buildingId);

        var occupant = Assert.Single(service.GetBuildingOccupants(planetId, buildingId));
        Assert.Equal(mapId, occupant.MapId);
        Assert.Equal(9, occupant.X);
        Assert.Equal(11, occupant.Y);

        await service.LeaveAllForUserAsync(1);
    }

    [Fact]
    public async Task LeaveAllForUser_ClearsPresenceWithoutKnowingThePlanet()
    {
        using var scope = CreateScope();
        var service = Resolve(scope);

        const long planetId = 900_009;
        const long mapId = 3;
        PrepareMap(scope, planetId, mapId);

        await service.JoinMapAsync(planetId, mapId, userId: 1, memberId: 11, "Ada", "a", 0, 0);
        Assert.Single(service.GetMapOccupants(planetId, mapId));

        // This is the disconnect path: the caller has only a user id.
        await service.LeaveAllForUserAsync(1);

        Assert.Empty(service.GetMapOccupants(planetId, mapId));
    }

    [Fact]
    public async Task LeaveMap_RemovesOnlyThatMember()
    {
        using var scope = CreateScope();
        var service = Resolve(scope);

        const long planetId = 900_010;
        const long mapId = 1;
        PrepareMap(scope, planetId, mapId);

        await service.JoinMapAsync(planetId, mapId, userId: 1, memberId: 11, "Ada", "a", 0, 0);
        await service.JoinMapAsync(planetId, mapId, userId: 2, memberId: 22, "Grace", "g", 1, 1);

        await service.LeaveMapAsync(planetId, mapId, 1);

        var remaining = Assert.Single(service.GetMapOccupants(planetId, mapId));
        Assert.Equal(2, remaining.UserId);

        await service.LeaveAllForUserAsync(2);
    }

    [Fact]
    public void GroupId_IsScopedPerPlanetAndMap()
    {
        // Movement on one map must not reach clients standing on another.
        Assert.Equal("v-5-9", VillagePresenceService.GetGroupId(5, 9));
        Assert.NotEqual(
            VillagePresenceService.GetGroupId(5, 9),
            VillagePresenceService.GetGroupId(5, 10));
    }

    [Fact]
    public async Task Move_RejectsBlockedAndOutOfBoundsDestinations()
    {
        using var scope = CreateScope();
        var service = Resolve(scope);

        const long planetId = 900_013;
        const long mapId = 1;
        PrepareMap(scope, planetId, mapId, blocked: [(2, 1)]);

        await service.JoinMapAsync(
            planetId, mapId, userId: 1, memberId: 11, "Ada", "a", 1, 1);

        Assert.False(service.Move(
            planetId, mapId, 1, 2, 1, VillageFacing.Right, null));

        var presence = Assert.Single(service.GetMapOccupants(planetId, mapId));
        Assert.Equal(1, presence.X);
        Assert.Equal(1, presence.Y);

        await service.LeaveAllForUserAsync(1);

        const long edgeMapId = 2;
        PrepareMap(scope, planetId, edgeMapId);
        await service.JoinMapAsync(
            planetId, edgeMapId, userId: 2, memberId: 22, "Grace", "g", 0, 0);

        Assert.False(service.Move(
            planetId, edgeMapId, 2, -1, 0, VillageFacing.Left, null));

        await service.LeaveAllForUserAsync(2);
    }

    [Fact]
    public async Task JoinMap_RejectsUnknownOrBlockedMapLocation()
    {
        using var scope = CreateScope();
        var service = Resolve(scope);

        const long planetId = 900_014;
        const long mapId = 1;
        PrepareMap(scope, planetId, mapId, blocked: [(4, 5)]);

        Assert.Null(await service.JoinMapAsync(
            planetId, mapId, userId: 1, memberId: 11, "Ada", "a", 4, 5));
        Assert.Null(await service.JoinMapAsync(
            planetId, mapId: 99, userId: 1, memberId: 11, "Ada", "a", 1, 1));
        Assert.Empty(service.GetMapOccupants(planetId, mapId));
    }
}
