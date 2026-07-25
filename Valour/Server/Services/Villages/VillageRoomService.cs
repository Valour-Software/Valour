using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Valour.Database.Context;
using Valour.Server.Database;
using Valour.Shared;
using Valour.Shared.Models;
using Valour.Shared.Villages;
using ChannelModel = Valour.Server.Models.Channel;

namespace Valour.Server.Services.Villages;

/// <summary>
/// Provides a temporary video-capable room and associated chat for village
/// buildings that are not linked to a permanent channel.
///
/// Planets are node-pinned, so the in-memory lease table is authoritative while
/// the room is alive. The backing channels exist only to reuse Valour's mature
/// chat and call transports; they are hidden from the normal directory and
/// soft-deleted shortly after the final occupant leaves.
/// </summary>
public sealed class VillageRoomService
{
    private const string DescriptionPrefix = "[village-ephemeral:";
    private static readonly TimeSpan EmptyRoomGracePeriod = TimeSpan.FromSeconds(20);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<VillageRoomService> _logger;
    private readonly ConcurrentDictionary<RoomKey, RoomState> _rooms = new();
    private readonly ConcurrentDictionary<RoomKey, SemaphoreSlim> _roomLocks = new();
    private readonly ConcurrentDictionary<long, Lazy<Task>> _planetInitializers = new();

    public VillageRoomService(
        IServiceScopeFactory scopeFactory,
        ILogger<VillageRoomService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<TaskResult<VillageEphemeralRoom>> AcquireAsync(
        long planetId,
        long buildingId,
        long userId)
    {
        await EnsurePlanetInitializedAsync(planetId);

        var key = new RoomKey(planetId, buildingId);
        var gate = _roomLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            if (_rooms.TryGetValue(key, out var existing))
            {
                existing.Occupants.Add(userId);
                existing.Generation++;
                return TaskResult<VillageEphemeralRoom>.FromData(existing.Room);
            }

            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
            var channelService = scope.ServiceProvider.GetRequiredService<ChannelService>();

            var building = await db.VillageBuildings
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == buildingId && x.PlanetId == planetId);
            if (building is null)
                return TaskResult<VillageEphemeralRoom>.FromFailure("Village building not found.");

            if (building.ChannelId is not null)
                return TaskResult<VillageEphemeralRoom>.FromFailure("This building already uses a permanent channel.");

            var channel = new ChannelModel
            {
                Name = MakeInternalChannelName(building.Name),
                Description = $"{DescriptionPrefix}{building.Id}] Temporary room for {building.Name}.",
                ChannelType = ChannelTypeEnum.PlanetVideo,
                PlanetId = planetId,
                ParentId = null,
                RawPosition = 0,
                InheritsPerms = false,
                IsDefault = false,
                Nsfw = false,
            };

            var created = await channelService.CreateAsync(channel);
            if (!created.Success || created.Data?.AssociatedChatChannelId is null)
            {
                return TaskResult<VillageEphemeralRoom>.FromFailure(
                    created.Message ?? "Could not create the village room.");
            }

            var room = new VillageEphemeralRoom
            {
                PlanetId = planetId,
                BuildingId = buildingId,
                ChannelId = created.Data.Id,
                ChatChannelId = created.Data.AssociatedChatChannelId.Value,
                Name = building.Name,
                SupportsVideo = true,
            };

            _rooms[key] = new RoomState(room, userId);
            return TaskResult<VillageEphemeralRoom>.FromData(room);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ReleaseAsync(long planetId, long buildingId, long userId)
    {
        var key = new RoomKey(planetId, buildingId);
        if (!_roomLocks.TryGetValue(key, out var gate))
            return;

        long generation;
        await gate.WaitAsync();
        try
        {
            if (!_rooms.TryGetValue(key, out var state))
                return;

            state.Occupants.Remove(userId);
            if (state.Occupants.Count > 0)
                return;

            generation = ++state.Generation;
        }
        finally
        {
            gate.Release();
        }

        _ = DeleteIfStillEmptyAfterGraceAsync(key, generation);
    }

    public async Task ReleaseAllForUserAsync(long userId)
    {
        foreach (var key in _rooms.Keys)
            await ReleaseAsync(key.PlanetId, key.BuildingId, userId);
    }

    private async Task DeleteIfStillEmptyAfterGraceAsync(RoomKey key, long generation)
    {
        await Task.Delay(EmptyRoomGracePeriod);

        if (!_roomLocks.TryGetValue(key, out var gate))
            return;

        VillageEphemeralRoom? room = null;
        await gate.WaitAsync();
        try
        {
            if (!_rooms.TryGetValue(key, out var state) ||
                state.Generation != generation ||
                state.Occupants.Count > 0)
            {
                return;
            }

            room = state.Room;
            _rooms.TryRemove(key, out _);
        }
        finally
        {
            gate.Release();
        }

        if (room is null)
            return;

        await DeleteChannelAsync(room.PlanetId, room.ChannelId);
    }

    /// <summary>
    /// A process exit can happen before the grace-period cleanup. The first room
    /// request after restart removes those orphaned channels before creating a
    /// new one, so temporary rooms never accumulate in the database.
    /// </summary>
    private Task EnsurePlanetInitializedAsync(long planetId)
    {
        var initializer = _planetInitializers.GetOrAdd(
            planetId,
            id => new Lazy<Task>(
                () => DeleteStaleChannelsAsync(id),
                LazyThreadSafetyMode.ExecutionAndPublication));
        return initializer.Value;
    }

    private async Task DeleteStaleChannelsAsync(long planetId)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
            var staleIds = await db.Channels
                .AsNoTracking()
                .Where(x =>
                    x.PlanetId == planetId &&
                    !x.IsDeleted &&
                    x.ChannelType == ChannelTypeEnum.PlanetVideo &&
                    x.Description.StartsWith(DescriptionPrefix))
                .Select(x => x.Id)
                .ToListAsync();

            foreach (var channelId in staleIds)
                await DeleteChannelAsync(planetId, channelId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean stale village rooms for planet {PlanetId}.", planetId);
        }
    }

    private async Task DeleteChannelAsync(long planetId, long channelId)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var channelService = scope.ServiceProvider.GetRequiredService<ChannelService>();
            var result = await channelService.DeletePlanetChannelAsync(planetId, channelId);
            if (!result.Success)
            {
                _logger.LogWarning(
                    "Failed to release village room channel {ChannelId}: {Message}",
                    channelId,
                    result.Message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to release village room channel {ChannelId}.", channelId);
        }
    }

    private static string MakeInternalChannelName(string buildingName)
    {
        var available = 32 - ISharedChannel.VillageEphemeralNamePrefix.Length;
        var safeName = string.IsNullOrWhiteSpace(buildingName) ? "Area" : buildingName.Trim();
        if (safeName.Length > available)
            safeName = safeName[..available];

        return ISharedChannel.VillageEphemeralNamePrefix + safeName;
    }

    private readonly record struct RoomKey(long PlanetId, long BuildingId);

    private sealed class RoomState
    {
        public RoomState(VillageEphemeralRoom room, long firstOccupant)
        {
            Room = room;
            Occupants.Add(firstOccupant);
        }

        public VillageEphemeralRoom Room { get; }
        public HashSet<long> Occupants { get; } = new();
        public long Generation { get; set; }
    }
}
