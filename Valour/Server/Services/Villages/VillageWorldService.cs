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
    private static readonly ConcurrentDictionary<long, SemaphoreSlim> SeedGates = new();
    private static readonly ConcurrentDictionary<(long PlanetId, long MapId), SemaphoreSlim> EditGates = new();

    private readonly ValourDb _db;
    private readonly CoreHubService _hubService;
    private readonly VillageRoomService _roomService;
    private readonly VillageCollisionService _collisionService;
    private readonly ILogger<VillageWorldService> _logger;

    public VillageWorldService(
        ValourDb db,
        CoreHubService hubService,
        VillageRoomService roomService,
        VillageCollisionService collisionService,
        ILogger<VillageWorldService> logger)
    {
        _db = db;
        _hubService = hubService;
        _roomService = roomService;
        _collisionService = collisionService;
        _logger = logger;
    }

    private const string DefaultTileset = "exterior-tileset-0";
    private const string GrassTexture = "/_content/Valour.Client/media/villages/default-tileset/terrain/grass-base-32.png";
    private const string InteriorFloorTexture = "/_content/Valour.Client/media/villages/default-tileset/terrain/stone-path-base-32.png";

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
        var maps = await _db.VillageMaps
            .Where(x => x.PlanetId == planet.Id)
            .OrderBy(x => x.Id)
            .ToListAsync();

        if (maps.Count == 0)
        {
            // Two first-open requests can arrive together. Without a
            // planet-scoped gate both observe an empty world and persist a
            // complete starter village, leaving duplicate outdoor maps and
            // buildings. Recheck after entering the gate because another
            // request may have finished seeding while this one waited.
            var gate = SeedGates.GetOrAdd(planet.Id, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try
            {
                maps = await _db.VillageMaps
                    .Where(x => x.PlanetId == planet.Id)
                    .OrderBy(x => x.Id)
                    .ToListAsync();

                if (maps.Count == 0)
                    await SeedWorldAsync(planet, channels);
            }
            finally
            {
                gate.Release();
            }

            maps = await _db.VillageMaps
                .Where(x => x.PlanetId == planet.Id)
                .OrderBy(x => x.Id)
                .ToListAsync();
        }

        return await BuildSceneAsync(planet, maps, channels, member, canManageVillage);
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
            var footprint = VillageObjectGeometry.GetFootprint(definition.Key);
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
                BaseTileTextureUrl = map.MapType == VillageMapType.Outdoor
                    ? GrassTexture
                    : InteriorFloorTexture,
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
                var footprint = VillageObjectGeometry.GetFootprint(item.DefinitionKey);
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
                    EntranceTile = new VillagePocPoint { X = building.DoorX, Y = building.DoorY },
                    CollisionRects =
                    {
                        // The doorway row is excluded from collision so the door
                        // is reachable without special-casing it in the runtime.
                        new VillagePocRect
                        {
                            X = building.X,
                            Y = building.Y,
                            Width = building.Width,
                            Height = Math.Max(1, building.Height - 1),
                        },
                    },
                });

                // A door leads in; the interior's own spawn tile leads back out.
                if (building.InteriorMapId is not null)
                {
                    var interior = maps.FirstOrDefault(x => x.Id == building.InteriorMapId.Value);

                    pocMap.Portals.Add(new VillagePocPortal
                    {
                        Kind = "Door",
                        X = building.DoorX,
                        Y = building.DoorY,
                        TargetMapId = building.InteriorMapId,
                        TargetX = interior?.SpawnX,
                        TargetY = interior?.SpawnY,
                        BuildingId = building.Id,
                        Color = "#fff2a8",
                    });
                }
            }

            // Interiors get an exit on their spawn tile leading back to the door
            // they were entered through.
            if (map.MapType == VillageMapType.Interior && map.ParentBuildingId is not null)
            {
                var parent = buildings.FirstOrDefault(x => x.Id == map.ParentBuildingId.Value);
                if (parent is not null)
                {
                    pocMap.Portals.Add(new VillagePocPortal
                    {
                        Kind = "Exit",
                        X = map.SpawnX,
                        Y = map.SpawnY,
                        TargetMapId = parent.MapId,
                        TargetX = parent.DoorX,
                        TargetY = parent.DoorY,
                        BuildingId = parent.Id,
                        Color = "#c9f0ff",
                    });
                }
            }

            scene.Maps.Add(pocMap);
        }

        return scene;
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

                var terrainKey = request.TerrainKey?.Trim() ?? string.Empty;
                if (terrainKey.Length == 0 && !string.IsNullOrWhiteSpace(request.DefinitionKey))
                    terrainKey = _collisionService.GetTerrainKey(map.TilesetKey, request.DefinitionKey.Trim());

                return await PaintTerrainAsync(
                    map,
                    actorMemberId,
                    canManageVillage,
                    terrainKey,
                    cells);
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

            var footprint = VillageObjectGeometry.GetFootprint(key);
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
                map, request.X, request.Y, footprint.Width, footprint.Height);
            if (placementError is not null)
                return TaskResult<VillageBuildResult>.FromFailure(placementError);

            var item = new Valour.Database.VillageObject
            {
                Id = IdManager.Generate(),
                PlanetId = planetId,
                MapId = mapId,
                DefinitionKey = definition.Key,
                X = request.X,
                Y = request.Y,
                ZIndex = 0,
                BlocksMovement = definition.BlocksMovement,
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
            .Where(item => item.PlanetId == map.PlanetId && item.MapId == map.Id && item.ZIndex < 0)
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
            .Where(item => item.PlanetId == map.PlanetId && item.MapId == map.Id && item.ZIndex < 0)
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
            return TaskResult<VillageBuildResult>.FromFailure("That item is no longer on the map.");

        var footprint = item.ZIndex < 0 ? (Width: 1, Height: 1) : VillageObjectGeometry.GetFootprint(item.DefinitionKey);
        if (!await CanEditBoundsAsync(
                map, actorMemberId, canManageVillage,
                item.X, item.Y, footprint.Width, footprint.Height))
        {
            return TaskResult<VillageBuildResult>.FromFailure("You cannot edit the property containing that item.");
        }

        if (item.ZIndex >= 0)
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
                                candidate.ZIndex < 0 &&
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
        int x,
        int y,
        int width,
        int height)
    {
        if (RectanglesOverlap(x, y, width, height, map.SpawnX, map.SpawnY, 1, 1))
            return "Keep the map's entrance clear.";

        var buildings = await _db.VillageBuildings
            .Where(item => item.PlanetId == map.PlanetId && item.MapId == map.Id)
            .ToListAsync();
        if (buildings.Any(item => RectanglesOverlap(
                x, y, width, height, item.X, item.Y, item.Width, item.Height)))
        {
            return "That space is occupied by a building.";
        }

        var objects = await _db.VillageObjects
            .Where(item => item.PlanetId == map.PlanetId && item.MapId == map.Id && item.ZIndex >= 0)
            .ToListAsync();
        if (objects.Any(item =>
            {
                var footprint = VillageObjectGeometry.GetFootprint(item.DefinitionKey);
                return RectanglesOverlap(x, y, width, height, item.X, item.Y, footprint.Width, footprint.Height);
            }))
        {
            return "That space is already furnished.";
        }

        return null;
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

    private static VillagePocDecoration ToDecoration(
        Valour.Database.VillageObject item,
        long localMemberId)
    {
        var footprint = VillageObjectGeometry.GetFootprint(item.DefinitionKey);
        return new VillagePocDecoration
        {
            Id = item.Id,
            Kind = item.DefinitionKey,
            DefinitionKey = item.DefinitionKey,
            X = item.X,
            Y = item.Y,
            Width = item.ZIndex < 0 ? 1 : footprint.Width,
            Height = item.ZIndex < 0 ? 1 : footprint.Height,
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

        await _db.SaveChangesAsync();
        _hubService.NotifyPlanetItemChange(planetId, plot.ToModel());
        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// Creates an immediately useful social world rather than an empty editor
    /// canvas: civic chat, voice and video venues surround a landscaped commons,
    /// while claimable property demonstrates the economy without requiring an
    /// administrator to author the first map by hand.
    /// </summary>
    private async Task SeedWorldAsync(PlanetModel planet, IEnumerable<ChannelModel> channels)
    {
        var channelList = channels.ToList();

        var primaryChat = channelList.FirstOrDefault(x => x.IsDefault)
            ?? channelList.FirstOrDefault(x => x.ChannelType == ChannelTypeEnum.PlanetChat);

        var voiceChannel = channelList.FirstOrDefault(x =>
            x.ChannelType == ChannelTypeEnum.PlanetVoice);

        var videoChannel = channelList.FirstOrDefault(x =>
            x.ChannelType == ChannelTypeEnum.PlanetVideo);

        var secondaryChat = channelList.FirstOrDefault(x =>
            x.ChannelType == ChannelTypeEnum.PlanetChat && x.Id != primaryChat?.Id);

        var hasPlanetCurrency = await _db.Currencies.AnyAsync(x => x.PlanetId == planet.Id);
        var starterPropertyPrice = hasPlanetCurrency ? 250m : 0m;

        var outdoor = new Valour.Database.VillageMap
        {
            Id = IdManager.Generate(),
            PlanetId = planet.Id,
            MapType = VillageMapType.Outdoor,
            Name = $"{planet.Name} Commons",
            Width = 52,
            Height = 40,
            TileSize = 32,
            SpawnX = 26,
            SpawnY = 25,
            TilesetKey = DefaultTileset,
            AmbientColor = "#fff4cf",
            Version = 1,
        };

        _db.VillageMaps.Add(outdoor);

        var blueprints = new[]
        {
            new
            {
                Name = "Town Hall",
                Description = "The village hearth: announcements, conversation, and community business.",
                X = 4, Y = 13, W = 8, H = 5,
                ChannelId = primaryChat?.Id,
                Voice = VillageVoiceMode.None,
                Sprite = "buildings.house-medium",
                ForSale = false,
            },
            new
            {
                Name = "Voice Lounge",
                Description = "A drop-in room built for natural, low-friction conversation.",
                X = 40, Y = 13, W = 8, H = 5,
                ChannelId = voiceChannel?.Id,
                Voice = voiceChannel is null ? VillageVoiceMode.None : VillageVoiceMode.LinkedChannel,
                Sprite = "buildings.house-medium.brown",
                ForSale = false,
            },
            new
            {
                Name = "Maker House",
                Description = "A claimable workshop for projects, clubs, and resident-led events.",
                X = 4, Y = 30, W = 8, H = 5,
                ChannelId = secondaryChat?.Id,
                Voice = VillageVoiceMode.None,
                Sprite = "buildings.house-medium.brown",
                ForSale = true,
            },
            new
            {
                Name = "Studio",
                Description = "A presentation-ready video room for stand-ups, demos, and broadcasts.",
                X = 40, Y = 30, W = 8, H = 5,
                ChannelId = videoChannel?.Id,
                Voice = videoChannel is null ? VillageVoiceMode.None : VillageVoiceMode.LinkedChannel,
                Sprite = "buildings.house-medium",
                ForSale = false,
            },
        };

        foreach (var blueprint in blueprints)
        {
            var interior = new Valour.Database.VillageMap
            {
                Id = IdManager.Generate(),
                PlanetId = planet.Id,
                MapType = VillageMapType.Interior,
                Name = $"{blueprint.Name} Interior",
                Width = 18,
                Height = 13,
                TileSize = 32,
                SpawnX = 9,
                SpawnY = 11,
                TilesetKey = DefaultTileset,
                AmbientColor = "#ffe8bd",
                Version = 1,
            };

            _db.VillageMaps.Add(interior);

            var plot = new Valour.Database.VillagePlot
            {
                Id = IdManager.Generate(),
                PlanetId = planet.Id,
                MapId = outdoor.Id,
                Name = $"{blueprint.Name} Grounds",
                X = blueprint.X - 1,
                Y = blueprint.Y - 1,
                Width = blueprint.W + 2,
                Height = blueprint.H + 2,
                EditMode = VillageEditMode.Owner,
                ForSale = false,
                Price = 0,
            };

            var building = new Valour.Database.VillageBuilding
            {
                Id = IdManager.Generate(),
                PlanetId = planet.Id,
                MapId = outdoor.Id,
                InteriorMapId = interior.Id,
                PlotId = plot.Id,
                Name = blueprint.Name,
                Description = blueprint.Description,
                X = blueprint.X,
                Y = blueprint.Y,
                Width = blueprint.W,
                Height = blueprint.H,
                // Bottom-centre, so the door sits on the wall facing the square.
                DoorX = blueprint.X + (blueprint.W / 2),
                DoorY = blueprint.Y + blueprint.H - 1,
                SpriteKey = blueprint.Sprite,
                VoiceMode = blueprint.Voice,
                ChannelId = blueprint.ChannelId,
                ForSale = blueprint.ForSale,
                Price = blueprint.ForSale ? starterPropertyPrice : 0,
            };

            _db.VillageBuildings.Add(building);
            _db.VillagePlots.Add(plot);
            interior.ParentBuildingId = building.Id;

            // A little furniture makes interiors read as meeting rooms instead
            // of differently coloured empty maps.
            _db.VillageObjects.AddRange(
                new Valour.Database.VillageObject
                {
                    Id = IdManager.Generate(),
                    PlanetId = planet.Id,
                    MapId = interior.Id,
                    DefinitionKey = "furniture.park-bench",
                    X = 5,
                    Y = 6,
                    BlocksMovement = true,
                },
                new Valour.Database.VillageObject
                {
                    Id = IdManager.Generate(),
                    PlanetId = planet.Id,
                    MapId = interior.Id,
                    DefinitionKey = "furniture.park-bench",
                    X = 11,
                    Y = 6,
                    BlocksMovement = true,
                });
        }

        // An empty parcel demonstrates land ownership independently from the
        // furnished Maker House listing.
        _db.VillagePlots.Add(new Valour.Database.VillagePlot
        {
            Id = IdManager.Generate(),
            PlanetId = planet.Id,
            MapId = outdoor.Id,
            Name = "Founder's Grove",
            X = 18,
            Y = 30,
            Width = 7,
            Height = 7,
            EditMode = VillageEditMode.Owner,
            ForSale = true,
            Price = hasPlanetCurrency ? 100m : 0m,
        });

        void AddObject(string key, int x, int y, bool blocksMovement = false, int zIndex = 0)
        {
            _db.VillageObjects.Add(new Valour.Database.VillageObject
            {
                Id = IdManager.Generate(),
                PlanetId = planet.Id,
                MapId = outdoor.Id,
                DefinitionKey = key,
                X = x,
                Y = y,
                ZIndex = zIndex,
                BlocksMovement = blocksMovement,
            });
        }

        void AddGround(string key, int x, int y) => AddObject(key, x, y, zIndex: -100);

        // A cross-shaped promenade and central plaza make routes legible at a
        // glance while preserving generous lawns for future construction.
        for (var y = 0; y < outdoor.Height; y++)
        {
            for (var x = 24; x <= 27; x++)
                AddGround("grass.dirt-path-flat", x, y);
        }

        for (var x = 0; x < outdoor.Width; x++)
        {
            for (var y = 20; y <= 23; y++)
                AddGround("grass.dirt-path-flat.2", x, y);
        }

        for (var y = 15; y <= 25; y++)
        {
            for (var x = 19; x <= 32; x++)
                AddGround("pathways.cobblestones", x, y);
        }

        foreach (var door in new[] { (8, 17), (44, 17), (8, 34), (44, 34) })
        {
            var from = Math.Min(door.Item2, 21);
            var to = Math.Max(door.Item2, 21);
            for (var y = from; y <= to; y++)
            {
                AddGround("grass.dirt-path-flat.1", door.Item1, y);
                AddGround("grass.dirt-path-flat.3", door.Item1 + 1, y);
            }
        }

        // Mature trees frame the world without creating a solid collision wall.
        for (var x = 2; x < outdoor.Width - 2; x += 5)
        {
            AddObject(x % 10 == 2 ? "trees.large-tree.with-grass" : "trees.large-tree", x, 5, blocksMovement: true);
            AddObject(x % 10 == 2 ? "large-tree-planter.square" : "trees.large-tree-planter", x, 39, blocksMovement: true);
        }

        for (var y = 10; y < outdoor.Height - 5; y += 6)
        {
            AddObject("small-tree.with-grass", 1, y, blocksMovement: true);
            AddObject("small-tree-planter.square", 50, y, blocksMovement: true);
        }

        // The commons has recognisable landmarks and quiet conversational
        // pockets, which also makes spatial voice distance easy to understand.
        AddObject("decor.stone-fountain", 25, 20, blocksMovement: true);
        AddObject("furniture.park-bench", 21, 18, blocksMovement: true);
        AddObject("furniture.park-bench", 29, 24, blocksMovement: true);
        AddObject("garden.planter.white", 18, 16);
        AddObject("garden.planter.yellow", 32, 16);
        AddObject("garden.planter.pink", 18, 25);
        AddObject("garden.flowers.white", 14, 20);
        AddObject("garden.flowers.pink", 34, 20);
        AddObject("garden.flowers.red", 34, 23);
        AddObject("commerce.market-stall", 28, 31, blocksMovement: true);

        await _db.SaveChangesAsync();

        _logger.LogInformation("Seeded starter village for planet {PlanetId}.", planet.Id);
    }
}
