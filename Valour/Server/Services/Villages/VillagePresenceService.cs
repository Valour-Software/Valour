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
    private readonly VillageCollisionService _collisionService;

    /// <summary>
    /// PlanetId -> UserId -> presence. Keyed by user rather than by map so a
    /// member can only ever occupy one map at a time, and so a disconnect can
    /// clean up without knowing which map they were on.
    /// </summary>
    private static readonly ConcurrentDictionary<long, ConcurrentDictionary<long, PresenceEntry>> Presences = new();

    /// <summary>
    /// Movement is tile-quantized and eased over 130ms client-side, so a member
    /// walking flat out produces roughly eight moves a second. Anything faster
    /// than this is either a client bug or someone poking the hub by hand.
    /// </summary>
    private const int MinMoveIntervalMs = 60;

    private static readonly ConcurrentDictionary<long, long> LastMoveTicks = new();

    public VillagePresenceService(
        CoreHubService hubService,
        VillageCollisionService collisionService)
    {
        _hubService = hubService;
        _collisionService = collisionService;
    }

    public static string GetGroupId(long planetId, long mapId) => $"v-{planetId}-{mapId}";

    /// <summary>
    /// Places a member on a map and returns the occupancy they should see.
    /// Joining a map implicitly leaves whichever map they were on before.
    /// </summary>
    public async Task<VillagePresenceSnapshot?> JoinMapAsync(
        long planetId,
        long mapId,
        long userId,
        long memberId,
        string name,
        string avatarUrl,
        int x,
        int y,
        long? buildingId = null,
        string? connectionId = null)
    {
        var collisionMap = await _collisionService.GetMapAsync(planetId, mapId);
        if (collisionMap is null || !collisionMap.IsWalkable(x, y))
            return null;

        var planetPresences = Presences.GetOrAdd(planetId, _ => new ConcurrentDictionary<long, PresenceEntry>());

        if (planetPresences.TryGetValue(userId, out var existing) && existing.Presence.MapId != mapId)
            NotifyPresenceLeft(planetId, existing.Presence.MapId, userId);

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
            // Spatial context belongs to the map, not to an untrusted packet.
            // In particular this prevents claiming an auto-room lease merely
            // by sending a building id while standing outside.
            BuildingId = collisionMap.ParentBuildingId,
        };

        planetPresences[userId] = new PresenceEntry(presence, connectionId);
        LastMoveTicks.TryRemove(userId, out _);

        _hubService.NotifyVillagePresenceJoined(planetId, mapId, presence);

        return new VillagePresenceSnapshot
        {
            PlanetId = planetId,
            MapId = mapId,
            // The joiner is included so a client can reconcile its own position
            // against what the server believes without a second round trip.
            Presences = planetPresences.Values
                .Select(x => x.Presence)
                .Where(x => x.MapId == mapId)
                .ToList(),
        };
    }

    /// <summary>
    /// Records a move and broadcasts it to everyone else on the same map.
    /// Returns false when the move was rejected as too frequent.
    /// </summary>
    public bool Move(
        long planetId,
        long mapId,
        long userId,
        int x,
        int y,
        VillageFacing facing,
        long? buildingId,
        string? connectionId = null)
    {
        if (!Presences.TryGetValue(planetId, out var planetPresences))
            return false;

        if (!planetPresences.TryGetValue(userId, out var entry) ||
            entry.Presence.MapId != mapId ||
            !entry.IsOwnedBy(connectionId))
        {
            return false;
        }

        var presence = entry.Presence;

        // A normal step changes exactly one axis by one tile. Door transitions
        // join a different map instead, so there is no legitimate same-map
        // teleport to preserve here. Reject before touching the throttle so a
        // forged jump cannot also suppress the member's next real step.
        var distance = Math.Abs(x - presence.X) + Math.Abs(y - presence.Y);
        if (distance != 1)
            return false;

        if (!_collisionService.IsWalkable(planetId, mapId, x, y))
            return false;

        if (!Enum.IsDefined(facing))
            return false;

        var now = DateTime.UtcNow.Ticks;
        var last = LastMoveTicks.GetValueOrDefault(userId);
        if (last != 0 && (now - last) < TimeSpan.TicksPerMillisecond * MinMoveIntervalMs)
            return false;

        LastMoveTicks[userId] = now;

        presence.X = x;
        presence.Y = y;
        presence.Facing = facing;
        // Building context is invariant for a map: outdoor maps have none and
        // an interior carries its persisted ParentBuildingId.
        var authoritativeBuildingId = presence.BuildingId;

        _hubService.NotifyVillagePresenceMoved(planetId, mapId, new VillagePresenceMove
        {
            PlanetId = planetId,
            MapId = mapId,
            UserId = userId,
            X = x,
            Y = y,
            Facing = facing,
            BuildingId = authoritativeBuildingId,
        });

        return true;
    }

    public Task<bool> LeaveMapAsync(
        long planetId,
        long mapId,
        long userId,
        string? connectionId = null) =>
        LeaveMapInternalAsync(planetId, mapId, userId, connectionId);

    /// <summary>
    /// Drops a member from whichever map they were on. Called on disconnect,
    /// where the caller does not know which map that was.
    /// </summary>
    public async Task<bool> LeaveAllAsync(long planetId, long userId, string? connectionId = null)
    {
        if (!Presences.TryGetValue(planetId, out var planetPresences))
            return false;

        if (!planetPresences.TryGetValue(userId, out var entry) || !entry.IsOwnedBy(connectionId))
            return false;

        return await LeaveMapInternalAsync(
            planetId,
            entry.Presence.MapId,
            userId,
            connectionId);
    }

    private Task<bool> LeaveMapInternalAsync(
        long planetId,
        long mapId,
        long userId,
        string? connectionId)
    {
        if (!Presences.TryGetValue(planetId, out var planetPresences) ||
            !planetPresences.TryGetValue(userId, out var entry) ||
            entry.Presence.MapId != mapId ||
            !entry.IsOwnedBy(connectionId) ||
            !planetPresences.TryRemove(new KeyValuePair<long, PresenceEntry>(userId, entry)))
        {
            return Task.FromResult(false);
        }

        if (planetPresences.IsEmpty)
            Presences.TryRemove(planetId, out _);

        LastMoveTicks.TryRemove(userId, out _);
        NotifyPresenceLeft(planetId, mapId, userId);
        return Task.FromResult(true);
    }

    /// <summary>
    /// Removes a member from whichever village they were in, without the caller
    /// needing to know the planet. Used on disconnect, where a ghost left
    /// standing in the world would otherwise persist until the node restarts.
    /// The scan is over members currently inside a village, not all members.
    /// </summary>
    public async Task<bool> LeaveAllForUserAsync(long userId, string? connectionId = null)
    {
        var removed = false;
        foreach (var (planetId, planetPresences) in Presences)
        {
            if (planetPresences.ContainsKey(userId))
                removed |= await LeaveAllAsync(planetId, userId, connectionId);
        }

        return removed;
    }

    /// <summary>
    /// Removes everyone from a planet's village when the feature is disabled.
    /// The presence-left broadcasts make already-open clients converge
    /// immediately rather than leaving ghost occupants until they reconnect.
    /// </summary>
    public async Task LeavePlanetAsync(long planetId)
    {
        if (!Presences.TryGetValue(planetId, out var planetPresences))
            return;

        foreach (var userId in planetPresences.Keys)
            await LeaveAllAsync(planetId, userId);
    }

    /// <summary>
    /// Everyone currently standing inside the given building, used to decide
    /// who belongs in its voice room.
    /// </summary>
    public IReadOnlyList<VillagePresence> GetBuildingOccupants(long planetId, long buildingId)
    {
        if (!Presences.TryGetValue(planetId, out var planetPresences))
            return Array.Empty<VillagePresence>();

        return planetPresences.Values
            .Select(x => x.Presence)
            .Where(x => x.BuildingId == buildingId)
            .ToList();
    }

    public IReadOnlyList<VillagePresence> GetMapOccupants(long planetId, long mapId)
    {
        if (!Presences.TryGetValue(planetId, out var planetPresences))
            return Array.Empty<VillagePresence>();

        return planetPresences.Values
            .Select(x => x.Presence)
            .Where(x => x.MapId == mapId)
            .ToList();
    }

    private void NotifyPresenceLeft(long planetId, long mapId, long userId) =>
        _hubService.NotifyVillagePresenceLeft(planetId, mapId, new VillagePresenceLeft
        {
            PlanetId = planetId,
            MapId = mapId,
            UserId = userId,
        });

    private sealed class PresenceEntry
    {
        public PresenceEntry(VillagePresence presence, string? connectionId)
        {
            Presence = presence;
            ConnectionId = connectionId;
        }

        public VillagePresence Presence { get; }
        public string? ConnectionId { get; }

        // A null expected id is reserved for service-level cleanup and tests.
        // Hub calls always provide their exact connection id.
        public bool IsOwnedBy(string? connectionId) =>
            connectionId is null || ConnectionId == connectionId;
    }
}
