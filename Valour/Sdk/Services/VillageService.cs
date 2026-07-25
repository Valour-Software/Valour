using Microsoft.AspNetCore.SignalR.Client;
using Valour.Sdk.Client;
using Valour.Sdk.Nodes;
using Valour.Shared;
using Valour.Shared.Utilities;
using Valour.Shared.Villages;

namespace Valour.Sdk.Services;

/// <summary>
/// Client access to villages: scene data over HTTP, and live occupancy over the
/// realtime hub.
///
/// Presence is intentionally not modelled as a cached <c>ClientModel</c>. It
/// changes several times a second per member and is worthless once stale, so it
/// lives in a plain dictionary here and is dropped wholesale when the member
/// leaves the map.
/// </summary>
public class VillageService : ServiceBase
{
    private readonly ValourClient _client;

    /// <summary>
    /// UserId -> presence, for the map this client is currently standing on.
    /// </summary>
    private readonly Dictionary<long, VillagePresence> _presences = new();

    private long _currentPlanetId;
    private long _currentMapId;

    /// <summary>
    /// Fired when someone joins, moves, or leaves the map this client is on.
    /// The renderer redraws every frame anyway, so this carries no payload -
    /// it exists so non-canvas UI can react.
    /// </summary>
    public HybridEvent PresenceChanged;

    public VillageService(ValourClient client)
    {
        _client = client;
        SetupLogging(client.Logger, new LogOptions("VillageService", "#6f8f4d", "#a3333e", "#a39433"));

        _client.NodeService.NodeAdded += HookHubEvents;
    }

    public Task<TaskResult<VillagePocScene>> FetchProofOfConceptSceneAsync(long planetId) =>
        _client.PrimaryNode.GetJsonAsync<VillagePocScene>($"api/planets/{planetId}/village/poc");

    /// <summary>
    /// Everyone currently visible on the joined map, excluding this client.
    /// </summary>
    public IReadOnlyCollection<VillagePresence> Presences => _presences.Values;

    private void HookHubEvents(Node node)
    {
        node.HubConnection.On<VillagePresence>("Village-Presence-Joined", presence =>
        {
            if (!node.AcceptsExternalPlanetRealtimeEvent(presence?.PlanetId))
                return;

            OnPresenceJoined(presence);
        });

        node.HubConnection.On<VillagePresenceMove>("Village-Presence-Moved", move =>
        {
            if (!node.AcceptsExternalPlanetRealtimeEvent(move?.PlanetId))
                return;

            OnPresenceMoved(move);
        });

        node.HubConnection.On<VillagePresenceLeft>("Village-Presence-Left", left =>
        {
            if (!node.AcceptsExternalPlanetRealtimeEvent(left?.PlanetId))
                return;

            OnPresenceLeft(left);
        });
    }

    /// <summary>
    /// Joins a village map's realtime group and seeds the occupancy snapshot.
    /// </summary>
    public async Task<TaskResult> JoinMapAsync(Planet planet, long mapId, int x, int y)
    {
        if (planet is null)
            return new TaskResult(false, "No planet.");

        if (_currentMapId != 0 && (_currentMapId != mapId || _currentPlanetId != planet.Id))
            await LeaveMapAsync();

        var snapshot = await planet.Node.HubConnection.InvokeAsync<VillagePresenceSnapshot>(
            "JoinVillageMap", planet.Id, mapId, x, y);

        if (snapshot is null)
            return new TaskResult(false, "Could not join the village map.");

        _currentPlanetId = planet.Id;
        _currentMapId = mapId;

        _presences.Clear();
        foreach (var presence in snapshot.Presences)
        {
            // The snapshot includes us; the local player is rendered from local
            // input rather than from the server's echo of our own position.
            if (presence.UserId == _client.Me?.Id)
                continue;

            _presences[presence.UserId] = presence;
        }

        PresenceChanged?.Invoke();
        return TaskResult.SuccessResult;
    }

    public async Task LeaveMapAsync()
    {
        if (_currentMapId == 0)
            return;

        var planetId = _currentPlanetId;
        var mapId = _currentMapId;

        _currentPlanetId = 0;
        _currentMapId = 0;
        _presences.Clear();
        PresenceChanged?.Invoke();

        if (!_client.Cache.Planets.TryGet(planetId, out var planet) || planet is null)
            return;

        try
        {
            await planet.Node.HubConnection.InvokeAsync("LeaveVillageMap", planetId, mapId);
        }
        catch
        {
            // A failed leave is not worth surfacing: the server drops presence
            // on disconnect anyway.
        }
    }

    /// <summary>
    /// Reports this client's new tile. Fire-and-forget by design - movement is
    /// already applied locally, and a dropped frame corrects itself on the next
    /// step rather than needing a retry.
    /// </summary>
    public void ReportMove(int x, int y, VillageFacing facing, long? buildingId)
    {
        if (_currentMapId == 0)
            return;

        if (!_client.Cache.Planets.TryGet(_currentPlanetId, out var planet) || planet is null)
            return;

        _ = planet.Node.HubConnection
            .SendAsync("MoveInVillage", _currentPlanetId, _currentMapId, x, y, (int)facing, buildingId)
            .ContinueWith(t =>
            {
                if (t.IsFaulted)
                    LogError("Failed to report village movement.", t.Exception);
            }, TaskContinuationOptions.OnlyOnFaulted);
    }

    private void OnPresenceJoined(VillagePresence presence)
    {
        if (presence.MapId != _currentMapId || presence.UserId == _client.Me?.Id)
            return;

        _presences[presence.UserId] = presence;
        PresenceChanged?.Invoke();
    }

    private void OnPresenceMoved(VillagePresenceMove move)
    {
        if (move.MapId != _currentMapId || move.UserId == _client.Me?.Id)
            return;

        if (!_presences.TryGetValue(move.UserId, out var presence))
        {
            // A move for someone we have no record of means we missed their
            // join - most likely a reconnect. Materialize them rather than
            // dropping them until they happen to rejoin.
            presence = new VillagePresence
            {
                PlanetId = move.PlanetId,
                MapId = move.MapId,
                UserId = move.UserId,
            };

            _presences[move.UserId] = presence;
        }

        presence.X = move.X;
        presence.Y = move.Y;
        presence.Facing = move.Facing;
        presence.BuildingId = move.BuildingId;

        PresenceChanged?.Invoke();
    }

    private void OnPresenceLeft(VillagePresenceLeft left)
    {
        if (left.MapId != _currentMapId)
            return;

        if (_presences.Remove(left.UserId))
            PresenceChanged?.Invoke();
    }
}
