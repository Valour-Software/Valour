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

    [Fact]
    public async Task JoinMap_PlacesMemberAndReturnsOccupancy()
    {
        using var scope = CreateScope();
        var service = Resolve(scope);

        const long planetId = 900_001;
        const long mapId = 1;

        var snapshot = await service.JoinMapAsync(planetId, mapId, userId: 1, memberId: 11, "Ada", "avatar-a", 4, 5);

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

        await service.JoinMapAsync(planetId, 1, userId: 1, memberId: 11, "Ada", "a", 0, 0);
        await service.JoinMapAsync(planetId, 2, userId: 1, memberId: 11, "Ada", "a", 3, 3);

        // Walking through a door must not leave a copy standing outside.
        Assert.Empty(service.GetMapOccupants(planetId, 1));
        Assert.Single(service.GetMapOccupants(planetId, 2));

        await service.LeaveAllForUserAsync(1);
    }

    [Fact]
    public async Task Move_UpdatesPositionFacingAndBuilding()
    {
        using var scope = CreateScope();
        var service = Resolve(scope);

        const long planetId = 900_005;
        const long mapId = 1;

        await service.JoinMapAsync(planetId, mapId, userId: 1, memberId: 11, "Ada", "a", 0, 0);

        var moved = service.Move(planetId, mapId, 1, 7, 8, VillageFacing.Left, buildingId: 42);
        Assert.True(moved);

        var presence = Assert.Single(service.GetMapOccupants(planetId, mapId));
        Assert.Equal(7, presence.X);
        Assert.Equal(8, presence.Y);
        Assert.Equal(VillageFacing.Left, presence.Facing);
        Assert.Equal(42, presence.BuildingId);

        await service.LeaveAllForUserAsync(1);
    }

    [Fact]
    public async Task Move_IsThrottled()
    {
        using var scope = CreateScope();
        var service = Resolve(scope);

        const long planetId = 900_006;
        const long mapId = 1;

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

        Assert.False(service.Move(planetId, 1, userId: 99, 1, 1, VillageFacing.Down, null));

        await service.JoinMapAsync(planetId, 1, userId: 1, memberId: 11, "Ada", "a", 0, 0);

        // Right user, wrong map.
        Assert.False(service.Move(planetId, 2, userId: 1, 1, 1, VillageFacing.Down, null));

        await service.LeaveAllForUserAsync(1);
    }

    [Fact]
    public async Task BuildingOccupants_TracksWhoIsInside()
    {
        using var scope = CreateScope();
        var service = Resolve(scope);

        const long planetId = 900_008;
        const long mapId = 1;
        const long buildingId = 7;

        await service.JoinMapAsync(planetId, mapId, userId: 1, memberId: 11, "Ada", "a", 0, 0);
        await service.JoinMapAsync(planetId, mapId, userId: 2, memberId: 22, "Grace", "g", 5, 5);

        service.Move(planetId, mapId, 1, 1, 1, VillageFacing.Down, buildingId);

        var occupants = service.GetBuildingOccupants(planetId, buildingId);
        Assert.Equal(1, Assert.Single(occupants).UserId);

        await service.LeaveAllForUserAsync(1);
        await service.LeaveAllForUserAsync(2);
    }

    [Fact]
    public async Task LeaveAllForUser_ClearsPresenceWithoutKnowingThePlanet()
    {
        using var scope = CreateScope();
        var service = Resolve(scope);

        const long planetId = 900_009;
        const long mapId = 3;

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
}
