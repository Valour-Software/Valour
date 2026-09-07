using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Valour.Database.Context;
using Valour.Server.Database;
using Valour.Shared;
using Valour.Shared.Models;
using Valour.Shared.Villages;
using ChannelModel = Valour.Server.Models.Channel;
using PlanetModel = Valour.Server.Models.Planet;
using PlanetMemberModel = Valour.Server.Models.PlanetMember;
using PlanetMemberEntity = Valour.Database.PlanetMember;

namespace Valour.Server.Services.Villages;

/// <summary>
/// Loads a planet's persisted village and seeds one the first time it is
/// opened.
///
/// The scene handed to the client keeps the same shape the proof-of-concept
/// used, so the canvas runtime did not have to be rewritten when the data moved
/// from being fabricated per request to being stored. What changed is that edits
/// now survive: the world is read from village_maps and its sibling tables
/// rather than rebuilt from the channel list every time.
/// </summary>
public class VillageWorldService
{
    private const int AutoTerrainZIndex = -100;
    private const int ManualTerrainZIndex = -101;
    private const int WallZIndex = 10;
    private static readonly ConcurrentDictionary<long, SemaphoreSlim> SeedGates = new();
    private static readonly ConcurrentDictionary<(long PlanetId, long MapId), SemaphoreSlim> EditGates = new();

    private readonly ValourDb _db;
    private readonly VillageTemplateService _templateService;
    private readonly CoreHubService _hubService;
    private readonly VillageRoomService _roomService;
    private readonly VillageCollisionService _collisionService;
    private readonly VillagePresenceService _presenceService;

    public VillageWorldService(
        ValourDb db,
        VillageTemplateService templateService,
        CoreHubService hubService,
        VillageRoomService roomService,
        VillageCollisionService collisionService,
        VillagePresenceService presenceService)
    {
        _db = db;
        _templateService = templateService;
        _hubService = hubService;
        _roomService = roomService;
        _collisionService = collisionService;
        _presenceService = presenceService;
    }

    /// <summary>
    /// Returns the planet's village, creating a starter world on first open.
    /// A planet that has villages enabled but no map yet would otherwise show an
    /// empty void, which reads as broken rather than as new.
    /// </summary>
    public async Task<VillagePocScene> GetOrCreateSceneAsync(
        PlanetModel planet,
        IEnumerable<ChannelModel> channels,
        PlanetMemberModel member,
        bool canManageVillage)
    {
        await EnsureWorldAsync(planet, channels, false);
        var maps = await _db.VillageMaps.Where(x => x.PlanetId == planet.Id).OrderBy(x => x.Id).ToListAsync();
        return await BuildSceneAsync(planet, maps, channels, member, canManageVillage);
    }

    public async Task ResetToTemplateAsync(PlanetModel planet, IEnumerable<ChannelModel> channels, long staffId)
    {
        await EnsureWorldAsync(planet, channels, true);
        _templateService.AddAudit(staffId, Valour.Shared.Models.Staff.StaffActionType.ResetVillage,
            $"Reset village {planet.Id} to the published default template.");
        await _db.SaveChangesAsync();
    }

    private async Task EnsureWorldAsync(PlanetModel planet, IEnumerable<ChannelModel> channels, bool forceReset)
    {
        var settings = await _templateService.GetSettingsAsync();
        var currentRevision = await _db.VillageMaps.Where(x => x.PlanetId == planet.Id && x.MapType == VillageMapType.Outdoor)
            .Select(x => (int?)x.TemplateRevision).FirstOrDefaultAsync();
        if (!forceReset && currentRevision is not null &&
            (settings.DraftPlanetId == planet.Id || currentRevision >= settings.ResetBeforeRevision)) return;
        if (forceReset && settings.DraftPlanetId == planet.Id)
            throw new InvalidOperationException("Choose another draft before resetting the template workshop.");

        var gate = SeedGates.GetOrAdd(planet.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        var deleted = new List<Valour.Server.Models.VillageBuilding>();
        try
        {
            await using var transaction = await _db.Database.BeginTransactionAsync();
            await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({planet.Id})");
            settings = await _templateService.GetSettingsAsync();
            if (forceReset && settings.DraftPlanetId == planet.Id)
                throw new InvalidOperationException("Choose another draft before resetting the template workshop.");
            currentRevision = await _db.VillageMaps.Where(x => x.PlanetId == planet.Id && x.MapType == VillageMapType.Outdoor)
                .Select(x => (int?)x.TemplateRevision).FirstOrDefaultAsync();
            if (!forceReset && currentRevision is not null &&
                (settings.DraftPlanetId == planet.Id || currentRevision >= settings.ResetBeforeRevision)) return;
            await _presenceService.LeavePlanetAsync(planet.Id);
            await _roomService.ClosePlanetRoomsAsync(planet.Id);
            var oldMaps = await _db.VillageMaps.IgnoreQueryFilters().AsNoTracking().Where(x => x.PlanetId == planet.Id).ToListAsync();
            deleted = (await _db.VillageBuildings.AsNoTracking().Where(x => x.PlanetId == planet.Id).ToListAsync())
                .Select(x => x.ToModel()).ToList();
            await _db.VillageObjects.Where(x => x.PlanetId == planet.Id).ExecuteDeleteAsync();
            await _db.VillageMapChunks.Where(x => x.PlanetId == planet.Id).ExecuteDeleteAsync();
            await _db.VillageBuildings.IgnoreQueryFilters().Where(x => x.PlanetId == planet.Id).ExecuteDeleteAsync();
            await _db.VillagePlots.Where(x => x.PlanetId == planet.Id).ExecuteDeleteAsync();
            await _db.VillageMaps.IgnoreQueryFilters().Where(x => x.PlanetId == planet.Id).ExecuteDeleteAsync();
            await _templateService.CopyPublishedAsync(planet.Id, channels, settings);
            await transaction.CommitAsync();
            foreach (var map in oldMaps) _collisionService.InvalidateMap(planet.Id, map.Id);
        }
        finally { gate.Release(); }
        foreach (var building in deleted) _hubService.NotifyPlanetItemDelete(planet.Id, building);
    }

    private async Task<VillagePocScene> BuildSceneAsync(
        PlanetModel planet,
        List<Valour.Database.VillageMap> maps,
        IEnumerable<ChannelModel> channels,
        PlanetMemberModel member,
        bool canManageVillage)
    {
        var mapIds = maps.Select(x => x.Id).ToList();

        var buildings = await _db.VillageBuildings
            .Where(x => x.PlanetId == planet.Id && mapIds.Contains(x.MapId))
            .ToListAsync();

        var plots = await _db.VillagePlots
            .Where(x => x.PlanetId == planet.Id && mapIds.Contains(x.MapId))
            .ToListAsync();

        var objects = await _db.VillageObjects
            .Where(x => x.PlanetId == planet.Id && mapIds.Contains(x.MapId))
            .ToListAsync();

        var ownerIds = buildings
            .Select(x => x.OwnerMemberId)
            .Concat(plots.Select(x => x.OwnerMemberId))
            .Where(x => x is not null)
            .Select(x => x!.Value)
            .Distinct()
            .ToList();

        var owners = ownerIds.Count == 0
            ? new Dictionary<long, PlanetMemberEntity>()
            : await _db.PlanetMembers
                .Include(x => x.User)
                .Where(x => x.PlanetId == planet.Id && ownerIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id);

        var currency = await _db.Currencies.FirstOrDefaultAsync(x => x.PlanetId == planet.Id);
        var channelLookup = channels.ToDictionary(x => x.Id);
        var defaultChat = channelLookup.Values.FirstOrDefault(x =>
                x.IsDefault && x.ChannelType == ChannelTypeEnum.PlanetChat)
            ?? channelLookup.Values.FirstOrDefault(x => x.ChannelType == ChannelTypeEnum.PlanetChat);
        var outdoor = maps.FirstOrDefault(x => x.MapType == VillageMapType.Outdoor) ?? maps[0];

        var scene = new VillagePocScene
        {
            PlanetId = planet.Id,
            LocalMemberId = member.Id,
            PlanetName = planet.Name,
            Title = $"{planet.Name} Village",
            Subtitle = "Meet naturally, build together, and make this world yours",
            CurrencySymbol = currency?.Symbol ?? string.Empty,
            CurrencyShortCode = currency?.ShortCode ?? string.Empty,
            CurrencyDecimalPlaces = currency?.DecimalPlaces ?? 0,
            DefaultChatChannelId = defaultChat?.Id,
            DefaultChatChannelName = defaultChat?.Name,
            StartingMapId = outdoor.Id,
            CanManageVillage = canManageVillage,
            BuildCatalogImageUrl = _collisionService.GetBuildCatalogImageUrl(outdoor.TilesetKey),
            BuildCatalogTileSize = _collisionService.GetBuildCatalogTileSize(outdoor.TilesetKey),
            Characters =
            {
                new VillagePocCharacter
                {
                    UserId = member.UserId,
                    Name = string.IsNullOrWhiteSpace(member.Nickname)
                        ? member.User?.Name ?? "You"
                        : member.Nickname,
                    MapId = outdoor.Id,
                    X = outdoor.SpawnX,
                    Y = outdoor.SpawnY,
                    IsLocalPlayer = true,
                    AvatarUrl = ISharedPlanetMember.GetAvatar(member, AvatarFormat.Webp64),
                    AccentColor = "#2d73d5",
                },
            },
        };

        foreach (var definition in _collisionService.GetBuildCatalog(outdoor.TilesetKey)
                     .Where(x => string.Equals(x.Kind, "Tile", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(x.Kind, "Sprite", StringComparison.OrdinalIgnoreCase)))
        {
            var footprint = _collisionService.GetFootprint(definition.Key);
            scene.BuildCatalog.Add(new VillagePocCatalogItem
            {
                Kind = definition.Kind,
                Name = definition.Name,
                Key = definition.Key,
                Category = GetCatalogCategory(definition.Key, definition.Kind),
                X = definition.X,
                Y = definition.Y,
                Width = definition.Width,
                Height = definition.Height,
                PlacementLayer = definition.PlacementLayer,
                SupportsItems = definition.SupportsItems,
                FootprintWidth = footprint.Width,
                FootprintHeight = footprint.Height,
                BlocksMovement = definition.BlocksMovement,
            });
        }

        foreach (var terrain in _collisionService.GetBuildTerrains(outdoor.TilesetKey))
        {
            scene.BuildTerrains.Add(new VillagePocTerrainItem
            {
                Key = terrain.Key,
                Name = terrain.Name,
                PreviewDefinitionKey = terrain.Preview.Key,
                X = terrain.Preview.X,
                Y = terrain.Preview.Y,
                Width = terrain.Preview.Width,
                Height = terrain.Preview.Height,
            });
        }

        foreach (var brush in _collisionService.GetBuildBrushes(outdoor.TilesetKey))
        {
            var previewCell = brush.Cells.ElementAtOrDefault((brush.Size * brush.Size) / 2);
            if (previewCell is null || previewCell.DefinitionKey.Length == 0)
                previewCell = brush.Cells.FirstOrDefault(x => x.DefinitionKey.Length > 0);
            if (previewCell is null ||
                !_collisionService.TryGetDefinition(outdoor.TilesetKey, previewCell.DefinitionKey, out var preview))
            {
                continue;
            }

            scene.BuildBrushes.Add(new VillagePocBrushItem
            {
                Key = brush.Key,
                Name = brush.Name,
                Size = brush.Size,
                PreviewDefinitionKey = preview.Key,
                X = preview.X,
                Y = preview.Y,
                Width = preview.Width,
                Height = preview.Height,
                Cells = brush.Cells.Select(cell => new VillagePocBrushCell
                {
                    DefinitionKey = cell.DefinitionKey,
                    Strength = cell.Strength,
                    Weight = cell.Weight,
                }).ToList(),
            });
        }

        foreach (var wallSet in _collisionService.GetBuildWallSets(outdoor.TilesetKey))
        {
            scene.BuildWallSets.Add(new VillagePocWallSet
            {
                Key = wallSet.Key,
                Layout = wallSet.Layout,
                Name = wallSet.Name,
                ImageUrl = wallSet.ImageUrl,
                TileSize = wallSet.TileSize,
                OriginX = wallSet.OriginX,
                OriginY = wallSet.OriginY,
                Columns = wallSet.Columns,
                Rows = wallSet.Rows,
                FrameCount = wallSet.FrameCount,
                PreviewFrame = wallSet.PreviewFrame,
                TopColor = wallSet.TopColor,
                FaceColor = wallSet.FaceColor,
            });
        }

        foreach (var map in maps)
        {
            var mapBuildings = buildings.Where(x => x.MapId == map.Id).ToList();

            var pocMap = new VillagePocMap
            {
                Id = map.Id,
                Name = map.Name,
                MapKind = map.MapType == VillageMapType.Outdoor ? "Outdoor" : "Interior",
                Width = map.Width,
                Height = map.Height,
                TileSize = map.TileSize,
                BackgroundColor = map.MapType == VillageMapType.Outdoor ? "#9fcf81" : "#d8c9a8",
                BaseTileTextureUrl = null,
                BaseTileDefinitionKey = map.MapType == VillageMapType.Outdoor ? "grass.short-grass" : "floor.oak",
                TilesetKey = map.TilesetKey,
                CanEdit = canManageVillage ||
                    (map.MapType == VillageMapType.Interior &&
                     map.ParentBuildingId is not null &&
                     buildings.Any(x => x.Id == map.ParentBuildingId.Value && x.OwnerMemberId == member.Id)),
                ParentBuildingId = map.ParentBuildingId,
                SpawnTile = new VillagePocPoint { X = map.SpawnX, Y = map.SpawnY },
            };

            foreach (var plot in plots.Where(x => x.MapId == map.Id))
            {
                pocMap.Plots.Add(new VillagePocPlot
                {
                    Id = plot.Id,
                    Name = plot.Name,
                    OwnerMemberId = plot.OwnerMemberId,
                    OwnerName = ResolveOwnerName(owners, plot.OwnerMemberId),
                    IsOwnedByLocalMember = plot.OwnerMemberId == member.Id,
                    CanEdit = canManageVillage || plot.EditMode == VillageEditMode.Everyone ||
                        (plot.EditMode == VillageEditMode.Owner && plot.OwnerMemberId == member.Id),
                    EditMode = plot.EditMode,
                    ForSale = plot.ForSale,
                    Price = plot.Price,
                    X = plot.X,
                    Y = plot.Y,
                    Width = plot.Width,
                    Height = plot.Height,
                });
            }

            foreach (var item in objects.Where(x => x.MapId == map.Id))
            {
                var footprint = _collisionService.GetFootprint(item.DefinitionKey);
                var decoration = new VillagePocDecoration
                {
                    Id = item.Id,
                    Kind = item.DefinitionKey,
                    DefinitionKey = item.DefinitionKey,
                    X = item.X,
                    Y = item.Y,
                    Width = footprint.Width,
                    Height = footprint.Height,
                    ZIndex = item.ZIndex,
                    Color = "#4e7a43",
                    BlocksMovement = item.BlocksMovement,
                    Rotation = item.Rotation,
                    OwnerMemberId = item.OwnerMemberId,
                    IsOwnedByLocalMember = item.OwnerMemberId == member.Id,
                };

                if (item.ZIndex < 0)
                    pocMap.GroundTiles.Add(decoration);
                else
                    pocMap.Decorations.Add(decoration);
            }

            foreach (var building in mapBuildings)
            {
                var (entranceTiles, primaryEntrance) = ResolveBuildingEntrances(map, building);
                channelLookup.TryGetValue(building.ChannelId ?? 0, out var channel);
                var chatChannelId = channel?.ChannelType == ChannelTypeEnum.PlanetChat
                    ? channel.Id
                    : channel?.AssociatedChatChannelId;
                channelLookup.TryGetValue(chatChannelId ?? 0, out var chatChannel);

                pocMap.Buildings.Add(new VillagePocBuilding
                {
                    Id = building.Id,
                    Name = building.Name,
                    X = building.X,
                    Y = building.Y,
                    Width = building.Width,
                    Height = building.Height,
                    Color = "#d7c29d",
                    RoofColor = "#8d6049",
                    Hint = building.Description ?? string.Empty,
                    SpriteKey = building.SpriteKey,
                    InteriorMapId = building.InteriorMapId,
                    ChannelId = building.ChannelId,
                    ChannelName = channel?.Name,
                    ChatChannelId = chatChannelId,
                    ChatChannelName = chatChannel?.Name,
                    ChannelType = channel?.ChannelType,
                    // An unlinked building receives a short-lived video-capable
                    // room (with associated chat) while occupied. Existing
                    // worlds therefore gain area communication without a data
                    // migration or an administrator wiring every property.
                    VoiceMode = building.ChannelId is null
                        ? VillageVoiceMode.AutoRoom
                        : building.VoiceMode,
                    OwnerMemberId = building.OwnerMemberId,
                    OwnerName = ResolveOwnerName(owners, building.OwnerMemberId),
                    IsOwnedByLocalMember = building.OwnerMemberId == member.Id,
                    ForSale = building.ForSale,
                    Price = building.Price,
                    EntranceTile = primaryEntrance,
                    EntranceTiles = entranceTiles,
                    CollisionRects = _collisionService.GetBuildingCollisionCells(
                        map.TilesetKey, building.SpriteKey, building.Width, building.Height)
                        .Select(cell => new VillagePocRect
                        {
                            X = building.X + cell.X, Y = building.Y + cell.Y, Width = 1, Height = 1,
                        }).ToList(),
                });
            }

            // Interiors get an exit on their spawn tile leading back to the door
            // they were entered through.
            if (map.MapType == VillageMapType.Interior && map.ParentBuildingId is not null)
            {
                var parent = buildings.FirstOrDefault(x => x.Id == map.ParentBuildingId.Value);
                if (parent is not null)
                {
                    var parentMap = maps.FirstOrDefault(candidate => candidate.Id == parent.MapId);
                    var primaryEntrance = parentMap is null
                        ? new VillagePocPoint { X = parent.DoorX, Y = parent.DoorY }
                        : ResolveBuildingEntrances(parentMap, parent).Primary;
                    var returnTile = await ResolveReturnTileAsync(planet.Id, parentMap, primaryEntrance, buildings);
                    pocMap.Portals.Add(new VillagePocPortal
                    {
                        Kind = "Exit",
                        X = map.SpawnX,
                        Y = map.SpawnY,
                        TargetMapId = parent.MapId,
                        TargetX = returnTile.X,
                        TargetY = returnTile.Y,
                        BuildingId = parent.Id,
                        Color = "#c9f0ff",
                    });
                }
            }

            scene.Maps.Add(pocMap);
        }

        return scene;
    }

    private async Task<VillagePocPoint> ResolveReturnTileAsync(long planetId, Valour.Database.VillageMap? map,
        VillagePocPoint entrance, List<Valour.Database.VillageBuilding> buildings)
    {
        if (map is null) return new() { X = entrance.X, Y = entrance.Y + 1 };
        var collision = await _collisionService.GetMapAsync(planetId, map.Id);
        var doors = buildings.Where(x => x.MapId == map.Id)
            .SelectMany(x => ResolveBuildingEntrances(map, x).Entrances).Select(x => (x.X, x.Y)).ToHashSet();
        var visited = new HashSet<(int X, int Y)> { (entrance.X, entrance.Y) };
        var queue = new Queue<(int X, int Y)>(); queue.Enqueue((entrance.X, entrance.Y));
        while (queue.TryDequeue(out var point))
            foreach (var next in new[] { (point.X, point.Y + 1), (point.X - 1, point.Y), (point.X + 1, point.Y), (point.X, point.Y - 1) })
            {
                if (collision?.IsWalkable(next.Item1, next.Item2) != true || !visited.Add(next)) continue;
                if (!doors.Contains(next)) return new() { X = next.Item1, Y = next.Item2 };
                queue.Enqueue(next);
            }
        return new() { X = map.SpawnX, Y = map.SpawnY };
    }

    private (List<VillagePocPoint> Entrances, VillagePocPoint Primary) ResolveBuildingEntrances(
        Valour.Database.VillageMap map,
        Valour.Database.VillageBuilding building)
    {
        var offsets = _collisionService.GetDoorOffsets(
            map.TilesetKey,
            building.SpriteKey,
            building.Width,
            building.Height);
        if (offsets.Count == 0)
        {
            var legacy = new VillagePocPoint { X = building.DoorX, Y = building.DoorY };
            return ([legacy], legacy);
        }

        var entrances = offsets.Select(offset => new VillagePocPoint
        {
            X = building.X + offset.X,
            Y = building.Y + offset.Y,
        }).ToList();
        var primary = entrances
            .OrderByDescending(entrance => entrance.Y)
            .ThenBy(entrance => Math.Abs((entrance.X - building.X + 0.5) - building.Width / 2d))
            .First();
        return (entrances, primary);
    }

    private static string? ResolveOwnerName(
        IReadOnlyDictionary<long, PlanetMemberEntity> owners,
        long? ownerMemberId)
    {
        if (ownerMemberId is null || !owners.TryGetValue(ownerMemberId.Value, out var owner))
            return null;

        return string.IsNullOrWhiteSpace(owner.Nickname)
            ? owner.User?.Name
            : owner.Nickname;
    }

    private static string GetCatalogCategory(string key, string kind)
    {
        if (string.Equals(kind, "Tile", StringComparison.OrdinalIgnoreCase))
            return "Surfaces";
        if (key.StartsWith("buildings.", StringComparison.OrdinalIgnoreCase))
            return "Buildings";
        if (key.StartsWith("office.", StringComparison.OrdinalIgnoreCase))
            return "Office";
        if (key.StartsWith("living.", StringComparison.OrdinalIgnoreCase))
            return "Living room";
        if (key.StartsWith("bedroom.", StringComparison.OrdinalIgnoreCase))
            return "Bedroom";
        if (key.StartsWith("kitchen.", StringComparison.OrdinalIgnoreCase))
            return "Kitchen";
        if (key.StartsWith("bathroom.", StringComparison.OrdinalIgnoreCase))
            return "Bathroom";
        if (key.StartsWith("studio.", StringComparison.OrdinalIgnoreCase))
            return "Studio";
        if (key.StartsWith("music.", StringComparison.OrdinalIgnoreCase))
            return "Music";
        if (key.StartsWith("recreation.", StringComparison.OrdinalIgnoreCase))
            return "Recreation";
        if (key.StartsWith("furniture.", StringComparison.OrdinalIgnoreCase))
            return "Furniture";
        if (key.StartsWith("garden.", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("tree", StringComparison.OrdinalIgnoreCase))
            return "Nature";
        if (key.StartsWith("commerce.", StringComparison.OrdinalIgnoreCase))
            return "Activities";
        return "Decor";
    }

    /// <summary>
    /// Applies one in-world build action after resolving the editable property
    /// from persisted ownership. The client only supplies intent and a tile;
    /// definition shape, collision, map bounds, and edit scope are authoritative.
    /// </summary>
    public async Task<TaskResult<VillageBuildResult>> EditMapAsync(
        long planetId,
        long mapId,
        long actorMemberId,
        bool canManageVillage,
        VillageBuildRequest request)
    {
        var gate = EditGates.GetOrAdd((planetId, mapId), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            var map = await _db.VillageMaps
                .FirstOrDefaultAsync(x => x.Id == mapId && x.PlanetId == planetId);
            if (map is null)
                return TaskResult<VillageBuildResult>.FromFailure("Village map not found.");

            if (request.Action == VillageBuildAction.Move)
                return await MoveObjectAsync(map, actorMemberId, canManageVillage, request);

            if (request.Action == VillageBuildAction.Erase)
                return await EraseObjectAsync(map, actorMemberId, canManageVillage, request.ObjectId);

            if (request.Action == VillageBuildAction.Paint)
            {
                var cells = request.Cells is { Count: > 0 }
                    ? request.Cells
                    : [new VillageBuildCell { X = request.X, Y = request.Y }];
                var brushKey = request.BrushKey?.Trim() ?? string.Empty;
                if (brushKey.Length > 0)
                {
                    if (!_collisionService.TryGetBrush(map.TilesetKey, brushKey, out var brush))
                        return TaskResult<VillageBuildResult>.FromFailure("That manual brush is not available on this map.");
                    return await PaintManualBrushAsync(map, actorMemberId, canManageVillage, brush, cells);
                }

                var definitionKey = request.DefinitionKey?.Trim() ?? string.Empty;
                if (definitionKey.Length > 0)
                {
                    if (!_collisionService.TryGetDefinition(map.TilesetKey, definitionKey, out var exactDefinition) ||
                        !string.Equals(exactDefinition.Kind, "Tile", StringComparison.OrdinalIgnoreCase) ||
                        exactDefinition.Width != 1 || exactDefinition.Height != 1)
                    {
                        return TaskResult<VillageBuildResult>.FromFailure(
                            "That exact tile is not available on this map.");
                    }

                    var exactBrush = new VillageCollisionService.BrushDefinition(
                        exactDefinition.Key,
                        exactDefinition.Name,
                        1,
                        [new VillageCollisionService.BrushCellDefinition(exactDefinition.Key, 1, 1)]);
                    return await PaintManualBrushAsync(
                        map, actorMemberId, canManageVillage, exactBrush, cells);
                }

                var terrainKey = request.TerrainKey?.Trim() ?? string.Empty;

                return await PaintTerrainAsync(
                    map,
                    actorMemberId,
                    canManageVillage,
                    terrainKey,
                    cells);
            }

            if (request.Action == VillageBuildAction.Wall)
            {
                var cells = request.Cells is { Count: > 0 }
                    ? request.Cells
                    : [new VillageBuildCell { X = request.X, Y = request.Y }];
                var wallSetKey = request.WallSetKey?.Trim() ?? string.Empty;
                if (!_collisionService.TryGetWallSet(map.TilesetKey, wallSetKey, out _))
                    return TaskResult<VillageBuildResult>.FromFailure("That wall style is not available on this map.");
                return await PaintWallsAsync(map, actorMemberId, canManageVillage, wallSetKey, cells);
            }

            if (request.Action != VillageBuildAction.Furnish)
                return TaskResult<VillageBuildResult>.FromFailure("Unknown village build action.");

            var key = request.DefinitionKey?.Trim() ?? string.Empty;
            if (!_collisionService.TryGetDefinition(map.TilesetKey, key, out var definition))
                return TaskResult<VillageBuildResult>.FromFailure("That catalog item is not available on this map.");

            if (!string.Equals(definition.Kind, "Sprite", StringComparison.OrdinalIgnoreCase))
            {
                return TaskResult<VillageBuildResult>.FromFailure("That item cannot be placed as furniture.");
            }

            var footprint = _collisionService.GetFootprint(key);
            if (!BoundsInsideMap(map, request.X, request.Y, footprint.Width, footprint.Height))
                return TaskResult<VillageBuildResult>.FromFailure("That item would extend beyond the map.");
            if (!await CanEditBoundsAsync(
                    map, actorMemberId, canManageVillage,
                    request.X, request.Y, footprint.Width, footprint.Height))
            {
                return TaskResult<VillageBuildResult>.FromFailure(
                    map.MapType == VillageMapType.Interior
                        ? "Only this building's owner can furnish its interior."
                        : "Place items entirely inside land you can edit.");
            }

            var placementError = await ValidateFurnishingPlacementAsync(
                map, request.X, request.Y, footprint.Width, footprint.Height, definition);
            if (placementError is not null)
                return TaskResult<VillageBuildResult>.FromFailure(placementError);

            if (definition.HasDoors)
            {
                return await PlaceBuildingAsync(
                    map,
                    actorMemberId,
                    definition,
                    footprint.Width,
                    footprint.Height,
                    request.X,
                    request.Y);
            }

            var item = new Valour.Database.VillageObject
            {
                Id = IdManager.Generate(),
                PlanetId = planetId,
                MapId = mapId,
                DefinitionKey = definition.Key,
                X = request.X,
                Y = request.Y,
                ZIndex = definition.PlacementLayer == "Floor" ? -10 : definition.PlacementLayer == "Surface" ? 5 : 0,
                BlocksMovement = definition.PlacementLayer == "Furniture" && definition.BlocksMovement,
                OwnerMemberId = actorMemberId,
            };

            _db.VillageObjects.Add(item);
            await _db.SaveChangesAsync();
            _collisionService.InvalidateMap(planetId, mapId);

            _hubService.NotifyPlanetItemChange(planetId, item.ToModel());

            var decoration = ToDecoration(item, actorMemberId);

            return TaskResult<VillageBuildResult>.FromData(new VillageBuildResult
            {
                Decoration = decoration,
                Decorations = [decoration],
            });
        }
        finally
        {
            gate.Release();
        }
    }

    private void SeedInterior(Valour.Database.VillageMap map, bool home, string style = "Home")
    {
        if (home) style = "Home";
        var divider = map.Width / 2;
        var right = divider + 2;
        void Add(string key, int x, int y, int zIndex = 0)
        {
            _collisionService.TryGetDefinition(map.TilesetKey, key, out var definition);
            _db.VillageObjects.Add(new Valour.Database.VillageObject
            {
                Id = IdManager.Generate(), PlanetId = map.PlanetId, MapId = map.Id,
                DefinitionKey = key, X = x, Y = y, ZIndex = zIndex,
                BlocksMovement = VillageWallTopology.IsWallDefinitionKey(key) ||
                    (zIndex == 0 && definition?.BlocksMovement == true),
            });
        }
        var floor = style switch { "Town Hall" => "floor.walnut", "Studio" => "floor.cream", _ => "floor.oak" };
        var accent = style switch { "Town Hall" => "floor.cream", "Studio" => "floor.walnut", "Voice Lounge" => "floor.limestone", _ => "floor.parquet" };
        for (var y = 0; y < map.Height; y++)
        for (var x = 0; x < map.Width; x++)
            Add(x > divider && y < map.Height - 4 ? accent : floor, x, y, -100);
        var walls = new HashSet<(int X, int Y)>();
        for (var x = 0; x < map.Width; x++)
        {
            walls.Add((x, 0));
            if (Math.Abs(x - map.SpawnX) > 1) walls.Add((x, map.Height - 1));
        }
        for (var y = 1; y < map.Height - 1; y++)
        {
            walls.Add((0, y)); walls.Add((map.Width - 1, y));
        }
        if (style != "Voice Lounge")
            for (var y = 1; y < map.Height - 5; y++)
                if (y < 6 || y > 8) walls.Add((divider, y));
        var wallStyle = style switch { "Town Hall" => "modern.green", "Studio" => "modern.blue", "Voice Lounge" => "modern.oak", _ => "modern.cream" };
        foreach (var wall in walls)
            Add(VillageWallTopology.MakeDefinitionKey(wallStyle,
                VillageWallTopology.ResolveFrame((x, y) => walls.Contains((x, y)), wall.X, wall.Y)), wall.X, wall.Y, WallZIndex);
        Add("decor.indoor-tree", 1, 3);
        Add("decor.indoor-plant", map.Width - 3, map.Height - 3);
        if (style == "Home")
        {
            Add("decor.rug.blue", 3, 5, -10);
            Add("living.sofa.linen", 3, 4);
            Add("living.coffee-table", 4, 7);
            Add("living.armchair.slate", 2, 7);
            Add("living.bookcase", 5, 2);
            Add("decor.floor-lamp.blue", 7, 4);
            Add("bedroom.bed.blue", right, 4);
            Add("bedroom.nightstand", right + 2, 5);
            Add("bedroom.dresser", right, 9);
            Add("living.cabinet.oak", right + 3, 2);
        }
        else if (style == "Town Hall")
        {
            foreach (var x in new[] { 4, 7, right, right + 3 }) Add("living.bookcase", x, 2);
            Add("decor.rug.red", 3, 7, -10);
            Add("living.sofa.amber", 3, 6);
            Add("living.coffee-table", 4, 9);
            Add("living.armchair.linen", 7, 8);
            Add("office.copier", 2, 11);
            Add("office.table.conference", right, 6);
            foreach (var x in new[] { right, right + 2, right + 4 })
            {
                Add("office.chair.padded", x, 5);
                Add("office.chair.blue.north", x, 9);
            }
            Add("decor.indoor-palm", map.Width - 3, 3);
        }
        else if (style == "Voice Lounge")
        {
            Add("living.sideboard", 4, 2);
            Add("living.cabinet.glass", 7, 2);
            Add("decor.rug.red", 3, 6, -10);
            Add("living.sofa.amber", 3, 5);
            Add("living.coffee-table", 4, 8);
            Add("living.armchair.linen", 2, 8);
            Add("living.armchair.linen", 7, 8);
            Add("decor.floor-lamp.red", 7, 5);
            foreach (var y in new[] { 4, 9 })
            {
                Add("living.table.birch", right + 1, y);
                Add("office.chair.light", right, y + 1);
                Add("office.chair.dark", right + 4, y + 1);
            }
            Add("decor.indoor-palm", right, 2);
            Add("living.sofa.linen", 4, 12);
        }
        else
        {
            foreach (var y in new[] { 4, 9 })
            foreach (var x in new[] { 2, 6 })
            {
                Add("office.desk.birch", x, y);
                Add("office.monitor", x, y, 5);
                Add("office.chair.blue.north", x + 1, y + 2);
            }
            Add("office.presentation-screen", right, 2);
            Add("office.lectern", right + 5, 4);
            Add("office.table.conference", right, 7);
            foreach (var x in new[] { right, right + 2, right + 4 })
            {
                Add("office.chair.padded", x, 6);
                Add("office.chair.blue.north", x, 10);
            }
            Add("living.sofa.slate", right + 1, 12);
        }
    }

    private async Task<TaskResult<VillageBuildResult>> PlaceBuildingAsync(
        Valour.Database.VillageMap map,
        long actorMemberId,
        VillageCollisionService.CollisionDefinition definition,
        int footprintWidth,
        int footprintHeight,
        int x,
        int y)
    {
        var doorOffsets = _collisionService.GetDoorOffsets(
            map.TilesetKey,
            definition.Key,
            footprintWidth,
            footprintHeight);
        if (doorOffsets.Count == 0)
        {
            return TaskResult<VillageBuildResult>.FromFailure(
                "That building's authored door does not land inside its ground footprint.");
        }

        // A multi-cell doorway has one stable return target: the lowest cell,
        // with the cell closest to horizontal centre breaking ties.
        var primaryDoor = doorOffsets
            .OrderByDescending(door => door.Y)
            .ThenBy(door => Math.Abs((door.X + 0.5) - footprintWidth / 2d))
            .First();
        var plotId = await _db.VillagePlots
            .Where(plot => plot.PlanetId == map.PlanetId &&
                           plot.MapId == map.Id &&
                           x >= plot.X && y >= plot.Y &&
                           (long)x + footprintWidth <= (long)plot.X + plot.Width &&
                           (long)y + footprintHeight <= (long)plot.Y + plot.Height)
            .Select(plot => (long?)plot.Id)
            .FirstOrDefaultAsync();
        var buildingId = IdManager.Generate();
        var interiorId = IdManager.Generate();
        var buildingName = definition.Name.Length <= ISharedVillageBuilding.MaxNameLength
            ? definition.Name
            : definition.Name[..ISharedVillageBuilding.MaxNameLength];
        var interiorName = $"{buildingName} Interior";
        if (interiorName.Length > ISharedVillageMap.MaxNameLength)
            interiorName = interiorName[..ISharedVillageMap.MaxNameLength];

        var interior = new Valour.Database.VillageMap
        {
            Id = interiorId,
            PlanetId = map.PlanetId,
            MapType = VillageMapType.Interior,
            Name = interiorName,
            ParentBuildingId = buildingId,
            Width = 18,
            Height = 13,
            TileSize = map.TileSize,
            SpawnX = 9,
            SpawnY = 11,
            TilesetKey = map.TilesetKey,
            AmbientColor = "#ffe8bd",
            Version = 1,
        };
        var building = new Valour.Database.VillageBuilding
        {
            Id = buildingId,
            PlanetId = map.PlanetId,
            MapId = map.Id,
            InteriorMapId = interiorId,
            PlotId = plotId,
            Name = buildingName,
            Description = "A resident-built property.",
            X = x,
            Y = y,
            Width = footprintWidth,
            Height = footprintHeight,
            DoorX = x + primaryDoor.X,
            DoorY = y + primaryDoor.Y,
            SpriteKey = definition.Key,
            OwnerMemberId = actorMemberId,
            VoiceMode = VillageVoiceMode.AutoRoom,
            ForSale = false,
            SaleId = string.Empty,
            Price = 0,
        };

        _db.VillageMaps.Add(interior);
        SeedInterior(interior, home: true);
        _db.VillageBuildings.Add(building);
        await _db.SaveChangesAsync();
        _collisionService.InvalidateMap(map.PlanetId, map.Id);
        _collisionService.InvalidateMap(map.PlanetId, interior.Id);
        _hubService.NotifyPlanetItemChange(map.PlanetId, building.ToModel());

        return TaskResult<VillageBuildResult>.FromData(new VillageBuildResult
        {
            SceneChanged = true,
            BuildingId = building.Id,
            InteriorMapId = interior.Id,
        });
    }

    private async Task<TaskResult<VillageBuildResult>> PaintTerrainAsync(
        Valour.Database.VillageMap map,
        long actorMemberId,
        bool canManageVillage,
        string terrainKey,
        IReadOnlyCollection<VillageBuildCell> cells)
    {
        if (string.IsNullOrWhiteSpace(terrainKey))
            return TaskResult<VillageBuildResult>.FromFailure("Choose a terrain before painting.");
        var targets = cells
            .Select(cell => (cell.X, cell.Y))
            .Distinct()
            .ToHashSet();
        if (targets.Count == 0)
            return TaskResult<VillageBuildResult>.FromFailure("Paint at least one tile.");
        if (targets.Count > 4096)
            return TaskResult<VillageBuildResult>.FromFailure("That terrain stroke is too large.");
        if (targets.Any(cell => !BoundsInsideMap(map, cell.X, cell.Y, 1, 1)))
            return TaskResult<VillageBuildResult>.FromFailure("That brush is outside the map.");
        if (!await CanEditTerrainCellsAsync(map, actorMemberId, canManageVillage, targets))
        {
            return TaskResult<VillageBuildResult>.FromFailure(
                map.MapType == VillageMapType.Interior
                    ? "Only this building's owner can paint its interior."
                    : "Paint inside land you can edit.");
        }

        // The logical terrain is recovered from each resolved definition. The
        // complete client stroke replaces the target cells in this in-memory
        // grid before any art is picked, so the whole stroke and its border see
        // one atomic terrain state.
        var ground = await _db.VillageObjects
            .Where(item => item.PlanetId == map.PlanetId && item.MapId == map.Id && item.ZIndex <= -100)
            .OrderBy(item => item.Id)
            .ToListAsync();
        var byPosition = ground
            .GroupBy(item => (item.X, item.Y))
            .ToDictionary(group => group.Key, group => group.ToList());

        string TerrainAt(int tileX, int tileY)
        {
            if (targets.Contains((tileX, tileY)))
                return terrainKey;
            if (byPosition.TryGetValue((tileX, tileY), out var items) && items.Count > 0)
                return items[0].ZIndex == AutoTerrainZIndex
                    ? _collisionService.GetTerrainKey(map.TilesetKey, items[0].DefinitionKey)
                    : string.Empty;
            return map.MapType == VillageMapType.Outdoor ? "grass" : string.Empty;
        }

        var affected = new HashSet<(int X, int Y)>();
        foreach (var target in targets)
        {
            for (var tileY = Math.Max(0, target.Y - 1); tileY <= Math.Min(map.Height - 1, target.Y + 1); tileY++)
            {
                for (var tileX = Math.Max(0, target.X - 1); tileX <= Math.Min(map.Width - 1, target.X + 1); tileX++)
                    affected.Add((tileX, tileY));
            }
        }

        // Resolve first, mutate second. If any requested terrain is unavailable,
        // the scoped DbContext remains untouched and cannot leak a partial edit
        // into a later save.
        var resolvedByPosition = new Dictionary<(int X, int Y), VillageCollisionService.CollisionDefinition>();
        foreach (var position in affected.OrderBy(cell => cell.Y).ThenBy(cell => cell.X))
        {
            var isTarget = targets.Contains(position);
            byPosition.TryGetValue(position, out var existingAtCell);
            if (!isTarget && (existingAtCell is null || existingAtCell.Count == 0))
                continue;

            var logicalTerrain = TerrainAt(position.X, position.Y);
            if (logicalTerrain.Length == 0 ||
                !_collisionService.TryResolveTerrainDefinition(
                    map.TilesetKey,
                    logicalTerrain,
                    TerrainAt,
                    map.Width,
                    map.Height,
                    position.X,
                    position.Y,
                    out var resolved))
            {
                if (isTarget)
                    return TaskResult<VillageBuildResult>.FromFailure("That terrain is not available on this map.");
                continue;
            }

            resolvedByPosition[position] = resolved;
        }

        // Reuse one object per coordinate so neighboring transition changes
        // preserve identity and ownership. Old duplicate ground entries are
        // removed defensively; the result tells clients to discard them too.
        var changed = new List<Valour.Database.VillageObject>();
        var removed = new List<Valour.Database.VillageObject>();
        var paintedByPosition = new Dictionary<(int X, int Y), Valour.Database.VillageObject>();
        foreach (var (position, resolved) in resolvedByPosition)
        {
            var isTarget = targets.Contains(position);
            byPosition.TryGetValue(position, out var existingAtCell);
            var item = existingAtCell?.FirstOrDefault();
            if (item is null)
            {
                item = new Valour.Database.VillageObject
                {
                    Id = IdManager.Generate(),
                    PlanetId = map.PlanetId,
                    MapId = map.Id,
                    X = position.X,
                    Y = position.Y,
                    ZIndex = AutoTerrainZIndex,
                };
                _db.VillageObjects.Add(item);
            }
            else if (existingAtCell!.Count > 1)
            {
                var duplicates = existingAtCell.Skip(1).ToList();
                removed.AddRange(duplicates);
                _db.VillageObjects.RemoveRange(duplicates);
            }

            var definitionChanged = item.DefinitionKey != resolved.Key ||
                                    item.BlocksMovement ||
                                    item.ZIndex != AutoTerrainZIndex;
            var ownerChanged = isTarget && item.OwnerMemberId != actorMemberId;
            item.DefinitionKey = resolved.Key;
            item.ZIndex = AutoTerrainZIndex;
            item.BlocksMovement = false;
            if (isTarget)
                item.OwnerMemberId = actorMemberId;

            if (definitionChanged || ownerChanged || isTarget)
                changed.Add(item);
            if (isTarget)
                paintedByPosition[position] = item;
        }

        var primaryPosition = (cells.First().X, cells.First().Y);
        if (!paintedByPosition.TryGetValue(primaryPosition, out var primary))
            return TaskResult<VillageBuildResult>.FromFailure("That terrain could not be painted.");

        await _db.SaveChangesAsync();
        _collisionService.InvalidateMap(map.PlanetId, map.Id);
        foreach (var oldItem in removed)
            _hubService.NotifyPlanetItemDelete(oldItem.ToModel());
        foreach (var item in changed)
            _hubService.NotifyPlanetItemChange(map.PlanetId, item.ToModel());

        var decorations = changed
            .Select(item => ToDecoration(item, actorMemberId))
            .ToList();
        return TaskResult<VillageBuildResult>.FromData(new VillageBuildResult
        {
            Decoration = ToDecoration(primary, actorMemberId),
            Decorations = decorations,
            RemovedObjectIds = removed.Select(item => item.Id).ToList(),
        });
    }

    private async Task<TaskResult<VillageBuildResult>> PaintManualBrushAsync(
        Valour.Database.VillageMap map,
        long actorMemberId,
        bool canManageVillage,
        VillageCollisionService.BrushDefinition brush,
        IReadOnlyCollection<VillageBuildCell> centers)
    {
        var uniqueCenters = centers
            .Select(cell => (cell.X, cell.Y))
            .Distinct()
            .ToList();
        if (uniqueCenters.Count == 0)
            return TaskResult<VillageBuildResult>.FromFailure("Paint at least one brush stamp.");
        if (uniqueCenters.Count > 4096)
            return TaskResult<VillageBuildResult>.FromFailure("That brush stroke is too large.");

        var radius = brush.Size / 2;
        var choices = new Dictionary<
            (int X, int Y),
            (VillageCollisionService.BrushCellDefinition Cell, VillageCollisionService.CollisionDefinition Definition)>();
        foreach (var center in uniqueCenters)
        {
            var originX = center.X - radius;
            var originY = center.Y - radius;
            for (var index = 0; index < brush.Cells.Count; index++)
            {
                var cell = brush.Cells[index];
                if (cell.DefinitionKey.Length == 0)
                    continue;

                var position = (X: originX + index % brush.Size, Y: originY + index / brush.Size);
                if (!BoundsInsideMap(map, position.X, position.Y, 1, 1))
                    return TaskResult<VillageBuildResult>.FromFailure("Keep the complete manual brush inside the map.");
                if (!_collisionService.TryGetDefinition(map.TilesetKey, cell.DefinitionKey, out var definition) ||
                    !string.Equals(definition.Kind, "Tile", StringComparison.OrdinalIgnoreCase))
                {
                    return TaskResult<VillageBuildResult>.FromFailure("That manual brush contains an unavailable tile.");
                }

                if (choices.TryGetValue(position, out var current) &&
                    (cell.Strength < current.Cell.Strength ||
                     (cell.Strength == current.Cell.Strength && cell.Weight < current.Cell.Weight)))
                {
                    continue;
                }

                choices[position] = (cell, definition);
            }
        }

        if (choices.Count == 0)
            return TaskResult<VillageBuildResult>.FromFailure("That manual brush has no paintable tiles.");
        if (choices.Count > 4096)
            return TaskResult<VillageBuildResult>.FromFailure("That brush stroke is too large.");
        if (!await CanEditTerrainCellsAsync(map, actorMemberId, canManageVillage, choices.Keys))
        {
            return TaskResult<VillageBuildResult>.FromFailure(
                map.MapType == VillageMapType.Interior
                    ? "Only this building's owner can paint its interior."
                    : "Keep the complete manual brush inside land you can edit.");
        }

        var ground = await _db.VillageObjects
            .Where(item => item.PlanetId == map.PlanetId && item.MapId == map.Id && item.ZIndex <= -100)
            .OrderBy(item => item.Id)
            .ToListAsync();
        var byPosition = ground
            .GroupBy(item => (item.X, item.Y))
            .ToDictionary(group => group.Key, group => group.ToList());

        string TerrainAt(int tileX, int tileY)
        {
            if (choices.ContainsKey((tileX, tileY)))
                return string.Empty;
            if (byPosition.TryGetValue((tileX, tileY), out var items) && items.Count > 0)
                return items[0].ZIndex == AutoTerrainZIndex
                    ? _collisionService.GetTerrainKey(map.TilesetKey, items[0].DefinitionKey)
                    : string.Empty;
            return map.MapType == VillageMapType.Outdoor ? "grass" : string.Empty;
        }

        var resolvedNeighbors = new Dictionary<(int X, int Y), VillageCollisionService.CollisionDefinition>();
        foreach (var target in choices.Keys)
        {
            for (var y = Math.Max(0, target.Y - 1); y <= Math.Min(map.Height - 1, target.Y + 1); y++)
            {
                for (var x = Math.Max(0, target.X - 1); x <= Math.Min(map.Width - 1, target.X + 1); x++)
                {
                    if (choices.ContainsKey((x, y)) ||
                        !byPosition.TryGetValue((x, y), out var items) ||
                        items.Count == 0 || items[0].ZIndex != AutoTerrainZIndex)
                    {
                        continue;
                    }

                    var terrainKey = TerrainAt(x, y);
                    if (terrainKey.Length > 0 &&
                        _collisionService.TryResolveTerrainDefinition(
                            map.TilesetKey, terrainKey, TerrainAt,
                            map.Width, map.Height, x, y, out var resolved))
                    {
                        resolvedNeighbors[(x, y)] = resolved;
                    }
                }
            }
        }

        var changed = new List<Valour.Database.VillageObject>();
        var removed = new List<Valour.Database.VillageObject>();
        Valour.Database.VillageObject? primary = null;
        foreach (var (position, choice) in choices)
        {
            byPosition.TryGetValue(position, out var existingAtCell);
            var item = existingAtCell?.FirstOrDefault();
            if (item is null)
            {
                item = new Valour.Database.VillageObject
                {
                    Id = IdManager.Generate(),
                    PlanetId = map.PlanetId,
                    MapId = map.Id,
                    X = position.X,
                    Y = position.Y,
                };
                _db.VillageObjects.Add(item);
            }
            else if (existingAtCell!.Count > 1)
            {
                var duplicates = existingAtCell.Skip(1).ToList();
                removed.AddRange(duplicates);
                _db.VillageObjects.RemoveRange(duplicates);
            }

            item.DefinitionKey = choice.Definition.Key;
            item.ZIndex = ManualTerrainZIndex;
            item.BlocksMovement = choice.Definition.BlocksMovement;
            item.OwnerMemberId = actorMemberId;
            changed.Add(item);
            primary ??= item;
        }

        foreach (var (position, definition) in resolvedNeighbors)
        {
            var neighbor = byPosition[position][0];
            if (neighbor.DefinitionKey == definition.Key)
                continue;
            neighbor.DefinitionKey = definition.Key;
            changed.Add(neighbor);
        }

        await _db.SaveChangesAsync();
        _collisionService.InvalidateMap(map.PlanetId, map.Id);
        foreach (var oldItem in removed)
            _hubService.NotifyPlanetItemDelete(oldItem.ToModel());
        foreach (var item in changed)
            _hubService.NotifyPlanetItemChange(map.PlanetId, item.ToModel());

        var decorations = changed.Select(item => ToDecoration(item, actorMemberId)).ToList();
        return TaskResult<VillageBuildResult>.FromData(new VillageBuildResult
        {
            Decoration = primary is null ? null : ToDecoration(primary, actorMemberId),
            Decorations = decorations,
            RemovedObjectIds = removed.Select(item => item.Id).ToList(),
        });
    }

    private async Task<TaskResult<VillageBuildResult>> PaintWallsAsync(
        Valour.Database.VillageMap map,
        long actorMemberId,
        bool canManageVillage,
        string wallSetKey,
        IReadOnlyCollection<VillageBuildCell> cells)
    {
        var targets = cells
            .Select(cell => (cell.X, cell.Y))
            .Distinct()
            .ToHashSet();
        if (targets.Count == 0)
            return TaskResult<VillageBuildResult>.FromFailure("Draw at least one wall tile.");
        if (targets.Count > 4096)
            return TaskResult<VillageBuildResult>.FromFailure("That wall stroke is too large.");
        if (targets.Any(cell => !BoundsInsideMap(map, cell.X, cell.Y, 1, 1)))
            return TaskResult<VillageBuildResult>.FromFailure("That wall is outside the map.");
        if (!await CanEditTerrainCellsAsync(map, actorMemberId, canManageVillage, targets))
        {
            return TaskResult<VillageBuildResult>.FromFailure(
                map.MapType == VillageMapType.Interior
                    ? "Only this building's owner can build walls in its interior."
                    : "Build walls inside land you can edit.");
        }
        if (_presenceService.GetMapOccupants(map.PlanetId, map.Id).Any(person => targets.Contains((person.X, person.Y))))
            return TaskResult<VillageBuildResult>.FromFailure("Keep the tiles occupied by people clear.");
        if (targets.Contains((map.SpawnX, map.SpawnY)))
            return TaskResult<VillageBuildResult>.FromFailure("Keep the map's entrance clear.");

        var buildings = await _db.VillageBuildings
            .Where(item => item.PlanetId == map.PlanetId && item.MapId == map.Id)
            .ToListAsync();
        if (targets.Any(target => buildings.Any(item => RectanglesOverlap(
                target.X, target.Y, 1, 1, item.X, item.Y, item.Width, item.Height))))
        {
            return TaskResult<VillageBuildResult>.FromFailure("A building already occupies part of that wall stroke.");
        }

        var mapObjects = await _db.VillageObjects
            .Where(item => item.PlanetId == map.PlanetId && item.MapId == map.Id && item.ZIndex >= 0)
            .OrderBy(item => item.Id)
            .ToListAsync();
        var nonWalls = mapObjects
            .Where(item => !VillageWallTopology.IsWallDefinitionKey(item.DefinitionKey))
            .ToList();
        if (targets.Any(target => nonWalls.Any(item =>
            {
                var footprint = _collisionService.GetFootprint(item.DefinitionKey);
                return RectanglesOverlap(
                    target.X, target.Y, 1, 1,
                    item.X, item.Y, footprint.Width, footprint.Height);
            })))
        {
            return TaskResult<VillageBuildResult>.FromFailure("Furniture already occupies part of that wall stroke.");
        }

        var wallsByPosition = mapObjects
            .Where(item => VillageWallTopology.IsWallDefinitionKey(item.DefinitionKey))
            .GroupBy(item => (item.X, item.Y))
            .ToDictionary(group => group.Key, group => group.ToList());
        var removed = new List<Valour.Database.VillageObject>();
        var targeted = new Dictionary<(int X, int Y), Valour.Database.VillageObject>();

        foreach (var position in targets)
        {
            wallsByPosition.TryGetValue(position, out var existing);
            var wall = existing?.FirstOrDefault();
            if (wall is null)
            {
                wall = new Valour.Database.VillageObject
                {
                    Id = IdManager.Generate(),
                    PlanetId = map.PlanetId,
                    MapId = map.Id,
                    X = position.X,
                    Y = position.Y,
                };
                _db.VillageObjects.Add(wall);
                wallsByPosition[position] = [wall];
            }
            else if (existing!.Count > 1)
            {
                var duplicates = existing.Skip(1).ToList();
                removed.AddRange(duplicates);
                _db.VillageObjects.RemoveRange(duplicates);
                wallsByPosition[position] = [wall];
            }

            wall.DefinitionKey = VillageWallTopology.MakeDefinitionKey(wallSetKey, 46);
            wall.ZIndex = WallZIndex;
            wall.BlocksMovement = true;
            wall.OwnerMemberId = actorMemberId;
            targeted[position] = wall;
        }

        var affected = new HashSet<(int X, int Y)>();
        foreach (var target in targets)
        {
            for (var y = Math.Max(0, target.Y - 1); y <= Math.Min(map.Height - 1, target.Y + 1); y++)
            {
                for (var x = Math.Max(0, target.X - 1); x <= Math.Min(map.Width - 1, target.X + 1); x++)
                    affected.Add((x, y));
            }
        }

        bool HasWall(int x, int y) => wallsByPosition.ContainsKey((x, y));
        var changed = new List<Valour.Database.VillageObject>();
        foreach (var position in affected.OrderBy(cell => cell.Y).ThenBy(cell => cell.X))
        {
            if (!wallsByPosition.TryGetValue(position, out var candidates) || candidates.Count == 0)
                continue;

            var wall = candidates[0];
            if (!VillageWallTopology.TryParseDefinitionKey(wall.DefinitionKey, out var style, out _))
                continue;
            var resolvedKey = VillageWallTopology.MakeDefinitionKey(
                style,
                VillageWallTopology.ResolveFrame(HasWall, position.X, position.Y));
            var wasTargeted = targeted.ContainsKey(position);
            if (wall.DefinitionKey != resolvedKey || wasTargeted)
            {
                wall.DefinitionKey = resolvedKey;
                changed.Add(wall);
            }
        }

        await _db.SaveChangesAsync();
        _collisionService.InvalidateMap(map.PlanetId, map.Id);
        foreach (var oldItem in removed)
            _hubService.NotifyPlanetItemDelete(oldItem.ToModel());
        foreach (var wall in changed)
            _hubService.NotifyPlanetItemChange(map.PlanetId, wall.ToModel());

        var decorations = changed.Select(wall => ToDecoration(wall, actorMemberId)).ToList();
        var primaryPosition = (cells.First().X, cells.First().Y);
        targeted.TryGetValue(primaryPosition, out var primary);
        return TaskResult<VillageBuildResult>.FromData(new VillageBuildResult
        {
            Decoration = primary is null ? null : ToDecoration(primary, actorMemberId),
            Decorations = decorations,
            RemovedObjectIds = removed.Select(item => item.Id).ToList(),
        });
    }

    private async Task<bool> HasSurfaceItemsAsync(Valour.Database.VillageObject item)
    {
        var footprint = _collisionService.GetFootprint(item.DefinitionKey);
        return item.ZIndex == 0 && await _db.VillageObjects.AnyAsync(other =>
            other.PlanetId == item.PlanetId && other.MapId == item.MapId && other.ZIndex == 5 &&
            other.X >= item.X && other.X < item.X + footprint.Width &&
            other.Y >= item.Y && other.Y < item.Y + footprint.Height);
    }

    private async Task<TaskResult<VillageBuildResult>> MoveObjectAsync(
        Valour.Database.VillageMap map, long actorMemberId, bool canManageVillage, VillageBuildRequest request)
    {
        var item = await _db.VillageObjects.FirstOrDefaultAsync(item =>
            item.Id == request.ObjectId && item.PlanetId == map.PlanetId && item.MapId == map.Id);
        if (item is null || item.ZIndex <= -100 || VillageWallTopology.IsWallDefinitionKey(item.DefinitionKey) ||
            !_collisionService.TryGetDefinition(map.TilesetKey, item.DefinitionKey, out var definition))
            return TaskResult<VillageBuildResult>.FromFailure("Choose furniture or a rug to move.");
        var footprint = _collisionService.GetFootprint(item.DefinitionKey);
        if (!BoundsInsideMap(map, request.X, request.Y, footprint.Width, footprint.Height) ||
            !await CanEditBoundsAsync(map, actorMemberId, canManageVillage, item.X, item.Y, footprint.Width, footprint.Height) ||
            !await CanEditBoundsAsync(map, actorMemberId, canManageVillage, request.X, request.Y, footprint.Width, footprint.Height))
            return TaskResult<VillageBuildResult>.FromFailure("Keep the furniture inside property you can edit.");
        if (await HasSurfaceItemsAsync(item))
            return TaskResult<VillageBuildResult>.FromFailure("Move the items on this desk or table first.");
        var error = await ValidateFurnishingPlacementAsync(map, request.X, request.Y,
            footprint.Width, footprint.Height, definition, item.Id);
        if (error is not null) return TaskResult<VillageBuildResult>.FromFailure(error);
        item.X = request.X;
        item.Y = request.Y;
        await _db.SaveChangesAsync();
        _collisionService.InvalidateMap(map.PlanetId, map.Id);
        _hubService.NotifyPlanetItemChange(map.PlanetId, item.ToModel());
        return TaskResult<VillageBuildResult>.FromData(new VillageBuildResult { Decoration = ToDecoration(item, actorMemberId) });
    }

    private async Task<TaskResult<VillageBuildResult>> EraseObjectAsync(
        Valour.Database.VillageMap map,
        long actorMemberId,
        bool canManageVillage,
        long? objectId)
    {
        if (objectId is null)
            return TaskResult<VillageBuildResult>.FromFailure("Choose an item to erase.");

        var item = await _db.VillageObjects.FirstOrDefaultAsync(x =>
            x.Id == objectId.Value && x.PlanetId == map.PlanetId && x.MapId == map.Id);
        if (item is null)
        {
            var building = await _db.VillageBuildings.FirstOrDefaultAsync(x =>
                x.Id == objectId.Value && x.PlanetId == map.PlanetId && x.MapId == map.Id);
            return building is null
                ? TaskResult<VillageBuildResult>.FromFailure("That item is no longer on the map.")
                : await ArchiveBuildingAsync(map, building, actorMemberId, canManageVillage);
        }

        var footprint = item.ZIndex <= -100 ? (Width: 1, Height: 1) : _collisionService.GetFootprint(item.DefinitionKey);
        if (!await CanEditBoundsAsync(
                map, actorMemberId, canManageVillage,
                item.X, item.Y, footprint.Width, footprint.Height))
        {
            return TaskResult<VillageBuildResult>.FromFailure("You cannot edit the property containing that item.");
        }

        if (await HasSurfaceItemsAsync(item))
            return TaskResult<VillageBuildResult>.FromFailure("Move or remove the items on this desk or table first.");

        if (VillageWallTopology.IsWallDefinitionKey(item.DefinitionKey))
            return await EraseWallAsync(map, item, actorMemberId);

        if (item.ZIndex > -100)
        {
            _db.VillageObjects.Remove(item);
            await _db.SaveChangesAsync();
            _collisionService.InvalidateMap(map.PlanetId, map.Id);
            _hubService.NotifyPlanetItemDelete(item.ToModel());

            return TaskResult<VillageBuildResult>.FromData(new VillageBuildResult
            {
                RemovedObjectIds = [item.Id],
            });
        }

        if (item.ZIndex == ManualTerrainZIndex)
        {
            _db.VillageObjects.Remove(item);
            await _db.SaveChangesAsync();
            _collisionService.InvalidateMap(map.PlanetId, map.Id);
            _hubService.NotifyPlanetItemDelete(item.ToModel());
            return TaskResult<VillageBuildResult>.FromData(new VillageBuildResult
            {
                RemovedObjectIds = [item.Id],
            });
        }

        // Removing terrain reveals the map's base surface. Resolve the eight
        // surviving neighbors against that new logical value in the same edit,
        // otherwise their edge art points at a terrain that no longer exists.
        var ground = await _db.VillageObjects
            .Where(candidate => candidate.PlanetId == map.PlanetId &&
                                candidate.MapId == map.Id &&
                                candidate.ZIndex <= -100 &&
                                candidate.Id != item.Id)
            .OrderBy(candidate => candidate.Id)
            .ToListAsync();
        var byPosition = ground
            .GroupBy(candidate => (candidate.X, candidate.Y))
            .ToDictionary(group => group.Key, group => group.ToList());

        string TerrainAt(int tileX, int tileY)
        {
            if (byPosition.TryGetValue((tileX, tileY), out var items) && items.Count > 0)
                return items[0].ZIndex == AutoTerrainZIndex
                    ? _collisionService.GetTerrainKey(map.TilesetKey, items[0].DefinitionKey)
                    : string.Empty;
            return map.MapType == VillageMapType.Outdoor ? "grass" : string.Empty;
        }

        var changed = new List<Valour.Database.VillageObject>();
        for (var tileY = Math.Max(0, item.Y - 1); tileY <= Math.Min(map.Height - 1, item.Y + 1); tileY++)
        {
            for (var tileX = Math.Max(0, item.X - 1); tileX <= Math.Min(map.Width - 1, item.X + 1); tileX++)
            {
                if (!byPosition.TryGetValue((tileX, tileY), out var candidates) || candidates.Count == 0)
                    continue;

                var neighbor = candidates[0];
                if (neighbor.ZIndex != AutoTerrainZIndex)
                    continue;
                var terrainKey = TerrainAt(tileX, tileY);
                if (terrainKey.Length == 0 ||
                    !_collisionService.TryResolveTerrainDefinition(
                        map.TilesetKey,
                        terrainKey,
                        TerrainAt,
                        map.Width,
                        map.Height,
                        tileX,
                        tileY,
                        out var resolved) ||
                    neighbor.DefinitionKey == resolved.Key)
                {
                    continue;
                }

                neighbor.DefinitionKey = resolved.Key;
                changed.Add(neighbor);
            }
        }

        _db.VillageObjects.Remove(item);
        await _db.SaveChangesAsync();
        _collisionService.InvalidateMap(map.PlanetId, map.Id);
        _hubService.NotifyPlanetItemDelete(item.ToModel());
        foreach (var neighbor in changed)
            _hubService.NotifyPlanetItemChange(map.PlanetId, neighbor.ToModel());

        return TaskResult<VillageBuildResult>.FromData(new VillageBuildResult
        {
            Decorations = changed.Select(neighbor => ToDecoration(neighbor, actorMemberId)).ToList(),
            RemovedObjectIds = [item.Id],
        });
    }

    private async Task<TaskResult<VillageBuildResult>> EraseWallAsync(
        Valour.Database.VillageMap map,
        Valour.Database.VillageObject wall,
        long actorMemberId)
    {
        var remainingWalls = await _db.VillageObjects
            .Where(item => item.PlanetId == map.PlanetId &&
                           item.MapId == map.Id &&
                           item.ZIndex >= 0 &&
                           item.Id != wall.Id &&
                           item.DefinitionKey.StartsWith(VillageWallTopology.DefinitionPrefix))
            .OrderBy(item => item.Id)
            .ToListAsync();
        remainingWalls = remainingWalls
            .Where(item => VillageWallTopology.IsWallDefinitionKey(item.DefinitionKey))
            .ToList();
        var byPosition = remainingWalls
            .GroupBy(item => (item.X, item.Y))
            .ToDictionary(group => group.Key, group => group.ToList());
        bool HasWall(int x, int y) => byPosition.ContainsKey((x, y));

        var changed = new List<Valour.Database.VillageObject>();
        for (var y = Math.Max(0, wall.Y - 1); y <= Math.Min(map.Height - 1, wall.Y + 1); y++)
        {
            for (var x = Math.Max(0, wall.X - 1); x <= Math.Min(map.Width - 1, wall.X + 1); x++)
            {
                if (!byPosition.TryGetValue((x, y), out var candidates) || candidates.Count == 0)
                    continue;
                var neighbor = candidates[0];
                if (!VillageWallTopology.TryParseDefinitionKey(neighbor.DefinitionKey, out var style, out _))
                    continue;

                var resolvedKey = VillageWallTopology.MakeDefinitionKey(
                    style,
                    VillageWallTopology.ResolveFrame(HasWall, x, y));
                if (neighbor.DefinitionKey == resolvedKey)
                    continue;
                neighbor.DefinitionKey = resolvedKey;
                changed.Add(neighbor);
            }
        }

        _db.VillageObjects.Remove(wall);
        await _db.SaveChangesAsync();
        _collisionService.InvalidateMap(map.PlanetId, map.Id);
        _hubService.NotifyPlanetItemDelete(wall.ToModel());
        foreach (var neighbor in changed)
            _hubService.NotifyPlanetItemChange(map.PlanetId, neighbor.ToModel());

        return TaskResult<VillageBuildResult>.FromData(new VillageBuildResult
        {
            Decorations = changed.Select(neighbor => ToDecoration(neighbor, actorMemberId)).ToList(),
            RemovedObjectIds = [wall.Id],
        });
    }

    private async Task<TaskResult<VillageBuildResult>> ArchiveBuildingAsync(
        Valour.Database.VillageMap map,
        Valour.Database.VillageBuilding building,
        long actorMemberId,
        bool canManageVillage)
    {
        if (!await CanEditBoundsAsync(
                map,
                actorMemberId,
                canManageVillage,
                building.X,
                building.Y,
                building.Width,
                building.Height))
        {
            return TaskResult<VillageBuildResult>.FromFailure(
                "You cannot remove the property containing that building.");
        }

        var archivedAt = DateTime.UtcNow;
        var rootInteriorId = building.InteriorMapId;
        var archivedMapIds = new HashSet<long>();
        var archivedBuildingIds = new HashSet<long>();
        var deletedModels = new List<Valour.Server.Models.VillageBuilding>();

        async Task ArchiveTreeAsync(Valour.Database.VillageBuilding current)
        {
            if (!archivedBuildingIds.Add(current.Id))
                return;

            deletedModels.Add(current.ToModel());
            if (current.InteriorMapId is not null)
            {
                var interior = await _db.VillageMaps.FirstOrDefaultAsync(candidate =>
                    candidate.PlanetId == map.PlanetId &&
                    candidate.Id == current.InteriorMapId.Value);
                if (interior is not null)
                {
                    var nested = await _db.VillageBuildings
                        .Where(candidate => candidate.PlanetId == map.PlanetId &&
                                            candidate.MapId == interior.Id)
                        .ToListAsync();
                    foreach (var child in nested)
                        await ArchiveTreeAsync(child);

                    interior.ArchivedAt = archivedAt;
                    archivedMapIds.Add(interior.Id);
                }
            }

            current.ArchivedAt = archivedAt;
            current.ForSale = false;
            current.SaleId = string.Empty;
        }

        await ArchiveTreeAsync(building);
        await _db.SaveChangesAsync();
        _collisionService.InvalidateMap(map.PlanetId, map.Id);
        foreach (var archivedMapId in archivedMapIds)
            _collisionService.InvalidateMap(map.PlanetId, archivedMapId);
        foreach (var deletedModel in deletedModels)
            _hubService.NotifyPlanetItemDelete(map.PlanetId, deletedModel);

        return TaskResult<VillageBuildResult>.FromData(new VillageBuildResult
        {
            SceneChanged = true,
            BuildingId = building.Id,
            InteriorMapId = rootInteriorId,
        });
    }

    private async Task<bool> CanEditTerrainCellsAsync(
        Valour.Database.VillageMap map,
        long actorMemberId,
        bool canManageVillage,
        IReadOnlyCollection<(int X, int Y)> cells)
    {
        if (canManageVillage)
            return true;

        if (map.MapType == VillageMapType.Interior)
        {
            return map.ParentBuildingId is not null &&
                   await _db.VillageBuildings.AnyAsync(building =>
                       building.PlanetId == map.PlanetId &&
                       building.Id == map.ParentBuildingId.Value &&
                       building.OwnerMemberId == actorMemberId);
        }

        var editablePlots = await _db.VillagePlots
            .Where(plot => plot.PlanetId == map.PlanetId &&
                           plot.MapId == map.Id &&
                           (plot.EditMode == VillageEditMode.Everyone ||
                            (plot.EditMode == VillageEditMode.Owner && plot.OwnerMemberId == actorMemberId)))
            .Select(plot => new { plot.X, plot.Y, plot.Width, plot.Height })
            .ToListAsync();

        return cells.All(cell => editablePlots.Any(plot =>
            cell.X >= plot.X && cell.Y >= plot.Y &&
            (long)cell.X < (long)plot.X + plot.Width &&
            (long)cell.Y < (long)plot.Y + plot.Height));
    }

    private async Task<bool> CanEditBoundsAsync(
        Valour.Database.VillageMap map,
        long actorMemberId,
        bool canManageVillage,
        int x,
        int y,
        int width,
        int height)
    {
        if (canManageVillage)
            return true;

        if (map.MapType == VillageMapType.Interior)
        {
            return map.ParentBuildingId is not null &&
                   await _db.VillageBuildings.AnyAsync(building =>
                       building.PlanetId == map.PlanetId &&
                       building.Id == map.ParentBuildingId.Value &&
                       building.OwnerMemberId == actorMemberId);
        }

        return await _db.VillagePlots.AnyAsync(plot =>
            plot.PlanetId == map.PlanetId &&
            plot.MapId == map.Id &&
            x >= plot.X && y >= plot.Y &&
            (long)x + width <= (long)plot.X + plot.Width &&
            (long)y + height <= (long)plot.Y + plot.Height &&
            (plot.EditMode == VillageEditMode.Everyone ||
             (plot.EditMode == VillageEditMode.Owner && plot.OwnerMemberId == actorMemberId)));
    }

    private async Task<string?> ValidateFurnishingPlacementAsync(
        Valour.Database.VillageMap map,
        int x, int y, int width, int height,
        VillageCollisionService.CollisionDefinition definition,
        long? excludedObjectId = null)
    {
        if (RectanglesOverlap(x, y, width, height, map.SpawnX, map.SpawnY, 1, 1))
            return "Keep the map's entrance clear.";
        if (definition.PlacementLayer == "Furniture" && definition.BlocksMovement &&
            _presenceService.GetMapOccupants(map.PlanetId, map.Id).Any(person =>
                RectanglesOverlap(x, y, width, height, person.X, person.Y, 1, 1)))
            return "Keep the tiles occupied by people clear.";
        var buildings = await _db.VillageBuildings
            .Where(item => item.PlanetId == map.PlanetId && item.MapId == map.Id).ToListAsync();
        if (buildings.Any(item => RectanglesOverlap(x, y, width, height, item.X, item.Y, item.Width, item.Height)))
            return "That space is occupied by a building.";
        var objects = await _db.VillageObjects
            .Where(item => item.PlanetId == map.PlanetId && item.MapId == map.Id && item.ZIndex > -100).ToListAsync();
        var supported = definition.PlacementLayer != "Surface";
        foreach (var item in objects)
        {
            if (item.Id == excludedObjectId) continue;
            var footprint = _collisionService.GetFootprint(item.DefinitionKey);
            if (!RectanglesOverlap(x, y, width, height, item.X, item.Y, footprint.Width, footprint.Height)) continue;
            if (definition.PlacementLayer == "Floor")
            {
                if (item.ZIndex == -10) return "There is already a rug in that space.";
                continue;
            }
            if (item.ZIndex < 0) continue;
            if (definition.PlacementLayer == "Surface" && item.ZIndex == 0 &&
                _collisionService.TryGetDefinition(map.TilesetKey, item.DefinitionKey, out var support) && support.SupportsItems &&
                x >= item.X && y >= item.Y && x + width <= item.X + footprint.Width && y + height <= item.Y + footprint.Height)
            {
                supported = true;
                continue;
            }
            return "That space is already furnished.";
        }
        return supported ? null : "Place this item on a desk or table.";
    }

    private static bool BoundsInsideMap(
        Valour.Database.VillageMap map,
        int x,
        int y,
        int width,
        int height) =>
        x >= 0 && y >= 0 && width > 0 && height > 0 &&
        (long)x + width <= map.Width && (long)y + height <= map.Height;

    private static bool RectanglesOverlap(
        int firstX, int firstY, int firstWidth, int firstHeight,
        int secondX, int secondY, int secondWidth, int secondHeight) =>
        firstX < secondX + secondWidth &&
        firstX + firstWidth > secondX &&
        firstY < secondY + secondHeight &&
        firstY + firstHeight > secondY;

    private VillagePocDecoration ToDecoration(
        Valour.Database.VillageObject item,
        long localMemberId)
    {
        var footprint = _collisionService.GetFootprint(item.DefinitionKey);
        return new VillagePocDecoration
        {
            Id = item.Id,
            Kind = VillageWallTopology.IsWallDefinitionKey(item.DefinitionKey)
                ? "Wall"
                : item.DefinitionKey,
            DefinitionKey = item.DefinitionKey,
            X = item.X,
            Y = item.Y,
            Width = item.ZIndex <= -100 ? 1 : footprint.Width,
            Height = item.ZIndex <= -100 ? 1 : footprint.Height,
            ZIndex = item.ZIndex,
            BlocksMovement = item.BlocksMovement,
            Rotation = item.Rotation,
            OwnerMemberId = item.OwnerMemberId,
            IsOwnedByLocalMember = item.OwnerMemberId == localMemberId,
        };
    }

    /// <summary>
    /// Renames or re-describes a building, and optionally rebinds its channel.
    /// The owner may edit their own property's identity; rebinding a channel is
    /// reserved for ManageVillage because it surfaces a planet channel to
    /// everyone who walks in.
    /// </summary>
    public async Task<TaskResult> UpdateBuildingAsync(
        long buildingId,
        long planetId,
        long actorMemberId,
        bool canManageVillage,
        VillageBuildingUpdateRequest request)
    {
        var building = await _db.VillageBuildings
            .FirstOrDefaultAsync(x => x.Id == buildingId && x.PlanetId == planetId);
        if (building is null)
            return new TaskResult(false, "Building not found.");

        var retireTemporaryRoom = request.UpdateChannel &&
                                  building.ChannelId is null &&
                                  request.ChannelId is not null;

        if (!canManageVillage && building.OwnerMemberId != actorMemberId)
            return new TaskResult(false, "Only the owner or a village manager can edit this building.");

        if (request.Name is not null)
        {
            var name = request.Name.Trim();
            if (name.Length == 0)
                return new TaskResult(false, "A building needs a name.");
            if (name.Length > ISharedVillageBuilding.MaxNameLength)
                return new TaskResult(false, $"Building names are limited to {ISharedVillageBuilding.MaxNameLength} characters.");

            building.Name = name;
        }

        if (request.Description is not null)
        {
            var description = request.Description.Trim();
            if (description.Length > ISharedVillageBuilding.MaxDescriptionLength)
                return new TaskResult(false, $"Descriptions are limited to {ISharedVillageBuilding.MaxDescriptionLength} characters.");

            building.Description = description;
        }

        if (request.UpdateChannel)
        {
            if (!canManageVillage)
                return new TaskResult(false, "Only a village manager can change a building's linked channel.");

            if (request.ChannelId is null)
            {
                // Unlinked buildings lease private area rooms on demand, so
                // clearing the channel is how a venue becomes private property.
                building.ChannelId = null;
                building.VoiceMode = VillageVoiceMode.None;
            }
            else
            {
                var channel = await _db.Channels.FirstOrDefaultAsync(x =>
                    x.Id == request.ChannelId.Value && x.PlanetId == planetId);
                if (channel is null)
                    return new TaskResult(false, "That channel does not belong to this planet.");

                if (channel.ChannelType is not (ChannelTypeEnum.PlanetChat
                    or ChannelTypeEnum.PlanetVoice
                    or ChannelTypeEnum.PlanetVideo))
                {
                    return new TaskResult(false, "Buildings can only surface chat, voice, or video channels.");
                }

                building.ChannelId = channel.Id;
                building.VoiceMode = channel.ChannelType == ChannelTypeEnum.PlanetChat
                    ? VillageVoiceMode.None
                    : VillageVoiceMode.LinkedChannel;
            }
        }

        await _db.SaveChangesAsync();
        if (retireTemporaryRoom)
            await _roomService.CloseBuildingRoomAsync(planetId, building.Id);

        _hubService.NotifyPlanetItemChange(planetId, building.ToModel());
        return TaskResult.SuccessResult;
    }

    public async Task<TaskResult> UpdatePlotAsync(
        long plotId,
        long planetId,
        long actorMemberId,
        bool canManageVillage,
        VillagePlotUpdateRequest request)
    {
        var plot = await _db.VillagePlots
            .FirstOrDefaultAsync(x => x.Id == plotId && x.PlanetId == planetId);
        if (plot is null)
            return new TaskResult(false, "Plot not found.");

        if (!canManageVillage && plot.OwnerMemberId != actorMemberId)
            return new TaskResult(false, "Only the owner or a village manager can edit this parcel.");

        if (request.Name is not null)
        {
            var name = request.Name.Trim();
            if (name.Length == 0)
                return new TaskResult(false, "A parcel needs a name.");
            if (name.Length > ISharedVillagePlot.MaxNameLength)
                return new TaskResult(false, $"Parcel names are limited to {ISharedVillagePlot.MaxNameLength} characters.");

            plot.Name = name;
        }

        if (request.X is not null || request.Y is not null || request.Width is not null ||
            request.Height is not null || request.EditMode is not null)
        {
            if (!canManageVillage)
                return new TaskResult(false, "Only a village manager can change parcel geometry.");
            if (request.EditMode is { } editMode && !Enum.IsDefined(editMode))
                return new TaskResult(false, "Choose a valid parcel edit mode.");

            var x = request.X ?? plot.X;
            var y = request.Y ?? plot.Y;
            var width = request.Width ?? plot.Width;
            var height = request.Height ?? plot.Height;
            var geometryError = await ValidatePlotGeometryAsync(
                planetId, plot.MapId, x, y, width, height, plot.Id);
            if (geometryError is not null)
                return new TaskResult(false, geometryError);

            plot.X = x;
            plot.Y = y;
            plot.Width = width;
            plot.Height = height;
            if (request.EditMode is not null)
                plot.EditMode = request.EditMode.Value;
        }

        await _db.SaveChangesAsync();
        _hubService.NotifyPlanetItemChange(planetId, plot.ToModel());
        return TaskResult.SuccessResult;
    }

    public async Task<TaskResult<VillagePocPlot>> CreatePlotAsync(
        long planetId, long mapId, VillagePlotCreateRequest request)
    {
        var map = await _db.VillageMaps.FirstOrDefaultAsync(x => x.Id == mapId && x.PlanetId == planetId);
        if (map is null)
            return TaskResult<VillagePocPlot>.FromFailure("Village map not found.");

        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length == 0 || name.Length > ISharedVillagePlot.MaxNameLength)
            return TaskResult<VillagePocPlot>.FromFailure(
                $"Parcel names must be between 1 and {ISharedVillagePlot.MaxNameLength} characters.");
        if (!Enum.IsDefined(request.EditMode))
            return TaskResult<VillagePocPlot>.FromFailure("Choose a valid parcel edit mode.");
        var geometryError = await ValidatePlotGeometryAsync(
            planetId, mapId, request.X, request.Y, request.Width, request.Height);
        if (geometryError is not null)
            return TaskResult<VillagePocPlot>.FromFailure(geometryError);

        var plot = new Valour.Database.VillagePlot
        {
            Id = IdManager.Generate(),
            PlanetId = planetId,
            MapId = mapId,
            Name = name,
            X = request.X,
            Y = request.Y,
            Width = request.Width,
            Height = request.Height,
            EditMode = request.EditMode,
            ForSale = false,
            Price = 0,
        };
        _db.VillagePlots.Add(plot);
        await _db.SaveChangesAsync();
        _hubService.NotifyPlanetItemChange(planetId, plot.ToModel());
        return TaskResult<VillagePocPlot>.FromData(ToAdminPlot(plot));
    }

    public async Task<TaskResult> DeletePlotAsync(long planetId, long plotId)
    {
        var plot = await _db.VillagePlots.FirstOrDefaultAsync(x => x.Id == plotId && x.PlanetId == planetId);
        if (plot is null)
            return new TaskResult(false, "Parcel not found.");

        var buildings = await _db.VillageBuildings
            .Where(x => x.PlanetId == planetId && x.PlotId == plotId).ToListAsync();
        foreach (var building in buildings)
            building.PlotId = null;
        _db.VillagePlots.Remove(plot);
        await _db.SaveChangesAsync();
        foreach (var building in buildings)
            _hubService.NotifyPlanetItemChange(planetId, building.ToModel());
        _hubService.NotifyPlanetItemDelete(plot.ToModel());
        return TaskResult.SuccessResult;
    }

    private async Task<string?> ValidatePlotGeometryAsync(
        long planetId, long mapId, int x, int y, int width, int height, long? exceptPlotId = null)
    {
        if (width < 1 || height < 1)
            return "A parcel must be at least one tile wide and high.";
        var map = await _db.VillageMaps.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == mapId && m.PlanetId == planetId);
        if (map is null)
            return "Village map not found.";
        if (x < 0 || y < 0 || (long)x + width > map.Width || (long)y + height > map.Height)
            return "Keep the parcel inside the map.";
        var overlaps = await _db.VillagePlots.AnyAsync(p =>
            p.PlanetId == planetId && p.MapId == mapId && p.Id != exceptPlotId &&
            x < p.X + p.Width && x + width > p.X &&
            y < p.Y + p.Height && y + height > p.Y);
        return overlaps ? "Parcels cannot overlap." : null;
    }

    private static VillagePocPlot ToAdminPlot(Valour.Database.VillagePlot plot) => new()
    {
        Id = plot.Id,
        Name = plot.Name,
        X = plot.X,
        Y = plot.Y,
        Width = plot.Width,
        Height = plot.Height,
        CanEdit = true,
        EditMode = plot.EditMode,
        BorderColor = "#5d8f4c",
        FillColor = "#00000000",
    };

}
