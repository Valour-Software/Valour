using System.Collections.Concurrent;
using Valour.Shared.Villages;

namespace Valour.Server.Services.Villages;

/// <summary>
/// Tracks who is standing where inside each village map.
///
/// This state is deliberately in-memory and per-node. Planets are node-pinned,
/// so every member of a given village is served by the same node, and losing
/// the state on restart is correct rather than lossy: clients re-announce
/// themselves when they reconnect. Persisting a position that changes several
/// times a second would be pure write amplification.
/// </summary>
public class VillagePresenceService
{
    private readonly CoreHubService _hubService;

    /// <summary>
    /// PlanetId -> UserId -> presence. Keyed by user rather than by map so a
    /// member can only ever occupy one map at a time, and so a disconnect can
    /// clean up without knowing which map they were on.
    /// </summary>
    private static readonly ConcurrentDictionary<long, ConcurrentDictionary<long, VillagePresence>> Presences = new();

    /// <summary>
    /// Movement is tile-quantized and eased over 130ms client-side, so a member
    /// walking flat out produces roughly eight moves a second. Anything faster
    /// than this is either a client bug or someone poking the hub by hand.
    /// </summary>
    private const int MinMoveIntervalMs = 60;

    private static readonly ConcurrentDictionary<long, long> LastMoveTicks = new();

    public VillagePresenceService(CoreHubService hubService)
    {
        _hubService = hubService;
    }

    public static string GetGroupId(long planetId, long mapId) => $"v-{planetId}-{mapId}";

    /// <summary>
    /// Places a member on a map and returns the occupancy they should see.
    /// Joining a map implicitly leaves whichever map they were on before.
    /// </summary>
    public async Task<VillagePresenceSnapshot> JoinMapAsync(
        long planetId,
        long mapId,
        long userId,
        long memberId,
        string name,
        string avatarUrl,
        int x,
        int y)
    {
        var planetPresences = Presences.GetOrAdd(planetId, _ => new ConcurrentDictionary<long, VillagePresence>());

        if (planetPresences.TryGetValue(userId, out var existing) && existing.MapId != mapId)
            await LeaveMapInternalAsync(planetId, existing.MapId, userId, removeFromPlanet: false);

        var presence = new VillagePresence
        {
            PlanetId = planetId,
            MapId = mapId,
            UserId = userId,
            MemberId = memberId,
            Name = name,
            AvatarUrl = avatarUrl,
            X = x,
            Y = y,
            Facing = VillageFacing.Down,
        };

        planetPresences[userId] = presence;

        _hubService.NotifyVillagePresenceJoined(planetId, mapId, presence);

        return new VillagePresenceSnapshot
        {
            PlanetId = planetId,
            MapId = mapId,
            // The joiner is included so a client can reconcile its own position
            // against what the server believes without a second round trip.
            Presences = planetPresences.Values.Where(x => x.MapId == mapId).ToList(),
        };
    }

    /// <summary>
    /// Records a move and broadcasts it to everyone else on the same map.
    /// Returns false when the move was rejected as too frequent.
    /// </summary>
    public bool Move(long planetId, long mapId, long userId, int x, int y, VillageFacing facing, long? buildingId)
    {
        if (!Presences.TryGetValue(planetId, out var planetPresences))
            return false;

        if (!planetPresences.TryGetValue(userId, out var presence) || presence.MapId != mapId)
            return false;

        var now = DateTime.UtcNow.Ticks;
        var last = LastMoveTicks.GetValueOrDefault(userId);
        if (last != 0 && (now - last) < TimeSpan.TicksPerMillisecond * MinMoveIntervalMs)
            return false;

        LastMoveTicks[userId] = now;

        presence.X = x;
        presence.Y = y;
        presence.Facing = facing;
        presence.BuildingId = buildingId;

        _hubService.NotifyVillagePresenceMoved(planetId, mapId, new VillagePresenceMove
        {
            PlanetId = planetId,
            MapId = mapId,
            UserId = userId,
            X = x,
            Y = y,
            Facing = facing,
            BuildingId = buildingId,
        });

        return true;
    }

    public Task LeaveMapAsync(long planetId, long mapId, long userId) =>
        LeaveMapInternalAsync(planetId, mapId, userId, removeFromPlanet: true);

    /// <summary>
    /// Drops a member from whichever map they were on. Called on disconnect,
    /// where the caller does not know which map that was.
    /// </summary>
    public async Task LeaveAllAsync(long planetId, long userId)
    {
        if (!Presences.TryGetValue(planetId, out var planetPresences))
            return;

        if (planetPresences.TryGetValue(userId, out var presence))
            await LeaveMapInternalAsync(planetId, presence.MapId, userId, removeFromPlanet: true);
    }

    private Task LeaveMapInternalAsync(long planetId, long mapId, long userId, bool removeFromPlanet)
    {
        if (Presences.TryGetValue(planetId, out var planetPresences) && removeFromPlanet)
        {
            planetPresences.TryRemove(userId, out _);

            if (planetPresences.IsEmpty)
                Presences.TryRemove(planetId, out _);
        }

        LastMoveTicks.TryRemove(userId, out _);

        _hubService.NotifyVillagePresenceLeft(planetId, mapId, new VillagePresenceLeft
        {
            PlanetId = planetId,
            MapId = mapId,
            UserId = userId,
        });

        return Task.CompletedTask;
    }

    /// <summary>
    /// Removes a member from whichever village they were in, without the caller
    /// needing to know the planet. Used on disconnect, where a ghost left
    /// standing in the world would otherwise persist until the node restarts.
    /// The scan is over members currently inside a village, not all members.
    /// </summary>
    public async Task LeaveAllForUserAsync(long userId)
    {
        foreach (var (planetId, planetPresences) in Presences)
        {
            if (planetPresences.ContainsKey(userId))
                await LeaveAllAsync(planetId, userId);
        }
    }

    /// <summary>
    /// Everyone currently standing inside the given building, used to decide
    /// who belongs in its voice room.
    /// </summary>
    public IReadOnlyList<VillagePresence> GetBuildingOccupants(long planetId, long buildingId)
    {
        if (!Presences.TryGetValue(planetId, out var planetPresences))
            return Array.Empty<VillagePresence>();

        return planetPresences.Values.Where(x => x.BuildingId == buildingId).ToList();
    }

    public IReadOnlyList<VillagePresence> GetMapOccupants(long planetId, long mapId)
    {
        if (!Presences.TryGetValue(planetId, out var planetPresences))
            return Array.Empty<VillagePresence>();

        return planetPresences.Values.Where(x => x.MapId == mapId).ToList();
    }
}
