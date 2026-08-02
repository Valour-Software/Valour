using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Valour.Database.Context;
using Valour.Shared.Villages;

namespace Valour.Server.Services.Villages;

/// <summary>
/// Loads and caches the authoritative walkability of village maps. Movement is
/// a hot path, so database work happens while joining a map and each subsequent
/// step is a bounds check plus a hash lookup.
/// </summary>
public sealed class VillageCollisionService
{
    private const int ChunkSize = 32;
    private const string DefaultTileset = "exterior-tileset-0";
    private const string DefaultTilesetResource =
        "Valour.Server.VillageTilesets.exterior-tileset-0.json";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<VillageCollisionService> _logger;
    private readonly ConcurrentDictionary<(long PlanetId, long MapId), Lazy<Task<VillageCollisionMap?>>> _maps = new();
    private readonly IReadOnlyDictionary<string, CollisionDefinition> _defaultDefinitions;
    private readonly IReadOnlyDictionary<string, TerrainIndexEntry> _defaultTerrainIndex;
    private readonly IReadOnlyDictionary<string, BrushDefinition> _defaultBrushes;
    private readonly string _defaultImageUrl;
    private readonly int _defaultTileSize;

    public VillageCollisionService(
        IServiceScopeFactory scopeFactory,
        ILogger<VillageCollisionService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        (_defaultDefinitions, _defaultTerrainIndex, _defaultBrushes, _defaultImageUrl, _defaultTileSize) = LoadDefinitions();
    }

    internal string GetBuildCatalogImageUrl(string? tilesetKey) =>
        string.Equals(tilesetKey, DefaultTileset, StringComparison.Ordinal)
            ? _defaultImageUrl
            : string.Empty;

    internal int GetBuildCatalogTileSize(string? tilesetKey) =>
        string.Equals(tilesetKey, DefaultTileset, StringComparison.Ordinal)
            ? _defaultTileSize
            : 16;

    internal IReadOnlyCollection<CollisionDefinition> GetBuildCatalog(string? tilesetKey) =>
        string.Equals(tilesetKey, DefaultTileset, StringComparison.Ordinal)
            ? _defaultDefinitions.Values.ToArray()
            : [];

    internal IReadOnlyCollection<TerrainCatalogDefinition> GetBuildTerrains(string? tilesetKey) =>
        string.Equals(tilesetKey, DefaultTileset, StringComparison.Ordinal)
            ? _defaultTerrainIndex.Values
                .Where(x => x.BaseTiles.Count > 0)
                .OrderBy(x => x.Terrain.Priority)
                .ThenBy(x => x.Terrain.Name, StringComparer.Ordinal)
                .Select(x => new TerrainCatalogDefinition(
                    x.Terrain.Key,
                    x.Terrain.Name,
                    PickWeighted(x.BaseTiles, 0, 0)!))
                .ToArray()
            : [];

    internal IReadOnlyCollection<BrushDefinition> GetBuildBrushes(string? tilesetKey) =>
        string.Equals(tilesetKey, DefaultTileset, StringComparison.Ordinal)
            ? _defaultBrushes.Values
                .OrderBy(x => x.Name, StringComparer.Ordinal)
                .ToArray()
            : [];

    internal bool TryGetBrush(
        string? tilesetKey,
        string brushKey,
        out BrushDefinition brush)
    {
        if (string.Equals(tilesetKey, DefaultTileset, StringComparison.Ordinal) &&
            _defaultBrushes.TryGetValue(brushKey, out var found))
        {
            brush = found;
            return true;
        }

        brush = default!;
        return false;
    }

    internal string GetTerrainKey(string? tilesetKey, string definitionKey) =>
        TryGetDefinition(tilesetKey, definitionKey, out var definition)
            ? definition.TerrainKey
            : string.Empty;

    internal IReadOnlyList<(int X, int Y)> GetDoorOffsets(
        string? tilesetKey,
        string definitionKey,
        int footprintWidth,
        int footprintHeight) =>
        TryGetDefinition(tilesetKey, definitionKey, out var definition)
            ? GetDoorOffsets(definition, footprintWidth, footprintHeight)
            : [];

    internal bool TryResolveTerrainDefinition(
        string? tilesetKey,
        string terrainKey,
        Func<int, int, string> getTerrainAt,
        int width,
        int height,
        int x,
        int y,
        out CollisionDefinition definition)
    {
        definition = default!;
        if (!string.Equals(tilesetKey, DefaultTileset, StringComparison.Ordinal) ||
            !_defaultTerrainIndex.ContainsKey(terrainKey))
        {
            return false;
        }

        var resolved = ResolveTerrainCell(
            terrainKey,
            getTerrainAt,
            width,
            height,
            x,
            y,
            _defaultTerrainIndex);
        if (resolved is null)
            return false;

        definition = resolved;
        return true;
    }

    internal bool TryGetDefinition(
        string? tilesetKey,
        string key,
        out CollisionDefinition definition)
    {
        if (string.Equals(tilesetKey, DefaultTileset, StringComparison.Ordinal) &&
            _defaultDefinitions.TryGetValue(key, out var found))
        {
            definition = found;
            return true;
        }

        definition = default!;
        return false;
    }

    internal Task<VillageCollisionMap?> GetMapAsync(long planetId, long mapId)
    {
        var lazy = _maps.GetOrAdd(
            (planetId, mapId),
            key => new Lazy<Task<VillageCollisionMap?>>(
                () => LoadMapAsync(key.PlanetId, key.MapId),
                LazyThreadSafetyMode.ExecutionAndPublication));

        return AwaitAndDiscardFailedLoadAsync((planetId, mapId), lazy);
    }

    internal bool IsWalkable(long planetId, long mapId, int x, int y) =>
        _maps.TryGetValue((planetId, mapId), out var lazy) &&
        lazy.IsValueCreated &&
        lazy.Value.IsCompletedSuccessfully &&
        lazy.Value.Result?.IsWalkable(x, y) == true;

    public void InvalidateMap(long planetId, long mapId) =>
        _maps.TryRemove((planetId, mapId), out _);

    private async Task<VillageCollisionMap?> AwaitAndDiscardFailedLoadAsync(
        (long PlanetId, long MapId) key,
        Lazy<Task<VillageCollisionMap?>> lazy)
    {
        try
        {
            return await lazy.Value;
        }
        catch
        {
            _maps.TryRemove(new KeyValuePair<(long PlanetId, long MapId), Lazy<Task<VillageCollisionMap?>>>(key, lazy));
            throw;
        }
    }

    private async Task<VillageCollisionMap?> LoadMapAsync(long planetId, long mapId)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ValourDb>();

        var map = await db.VillageMaps
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.PlanetId == planetId && x.Id == mapId);
        if (map is null)
            return null;

        var objects = await db.VillageObjects
            .AsNoTracking()
            .Where(x => x.PlanetId == planetId && x.MapId == mapId && x.BlocksMovement)
            .ToListAsync();
        var buildings = await db.VillageBuildings
            .AsNoTracking()
            .Where(x => x.PlanetId == planetId && x.MapId == mapId)
            .ToListAsync();
        var chunks = await db.VillageMapChunks
            .AsNoTracking()
            .Where(x => x.PlanetId == planetId && x.MapId == mapId)
            .ToListAsync();

        var definitions = string.Equals(map.TilesetKey, DefaultTileset, StringComparison.Ordinal)
            ? _defaultDefinitions
            : EmptyDefinitions.Instance;

        return VillageCollisionMap.Build(map, objects, buildings, chunks, definitions, _logger);
    }

    private (
        IReadOnlyDictionary<string, CollisionDefinition> Definitions,
        IReadOnlyDictionary<string, TerrainIndexEntry> TerrainIndex,
        IReadOnlyDictionary<string, BrushDefinition> Brushes,
        string ImageUrl,
        int TileSize) LoadDefinitions()
    {
        try
        {
            using var stream = typeof(VillageCollisionService).Assembly
                .GetManifestResourceStream(DefaultTilesetResource);
            if (stream is null)
            {
                _logger.LogError("Embedded village tileset {Resource} was not found.", DefaultTilesetResource);
                return (EmptyDefinitions.Instance, EmptyTerrainIndex.Instance, EmptyBrushes.Instance, string.Empty, 16);
            }

            using var document = JsonDocument.Parse(stream);
            var definitions = new Dictionary<string, CollisionDefinition>(StringComparer.Ordinal);
            var terrains = new Dictionary<string, TerrainDefinition>(StringComparer.Ordinal);
            var imageUrl = document.RootElement.TryGetProperty("image", out var image) &&
                           image.ValueKind == JsonValueKind.String
                ? image.GetString() ?? string.Empty
                : string.Empty;
            var tileSize = document.RootElement.TryGetProperty("tileSize", out var tileSizeElement) &&
                           tileSizeElement.TryGetInt32(out var parsedTileSize)
                ? Math.Max(1, parsedTileSize)
                : 16;

            if (document.RootElement.TryGetProperty("terrains", out var rawTerrains) &&
                rawTerrains.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in rawTerrains.EnumerateArray())
                {
                    if (!TryGetString(item, "Key", "key", out var key))
                        continue;

                    TryGetString(item, "Name", "name", out var name);
                    TryGetInt(item, "Priority", "priority", out var priority);
                    terrains[key] = new TerrainDefinition(
                        key,
                        string.IsNullOrWhiteSpace(name) ? key : name,
                        priority);
                }
            }

            if (!document.RootElement.TryGetProperty("definitions", out var rawDefinitions) ||
                rawDefinitions.ValueKind != JsonValueKind.Array)
            {
                return (definitions, BuildTerrainIndex(terrains.Values, definitions.Values), EmptyBrushes.Instance, imageUrl, tileSize);
            }

            foreach (var item in rawDefinitions.EnumerateArray())
            {
                if (!TryGetString(item, "Key", "key", out var key) ||
                    !TryGetInt(item, "Width", "width", out var width) ||
                    !TryGetInt(item, "Height", "height", out var height) ||
                    !TryGetProperty(item, "Collision", "collision", out var collision) ||
                    collision.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                TryGetString(item, "Kind", "kind", out var kind);
                TryGetString(item, "Name", "name", out var name);
                TryGetInt(item, "X", "x", out var x);
                TryGetInt(item, "Y", "y", out var y);
                TryGetString(item, "TerrainKey", "terrainKey", out var terrainKey);
                TryGetString(item, "TerrainRole", "terrainRole", out var terrainRole);
                TryGetString(item, "TerrainDirection", "terrainDirection", out var terrainDirection);
                TryGetString(item, "TerrainAgainst", "terrainAgainst", out var terrainAgainst);
                TryGetInt(item, "TerrainWeight", "terrainWeight", out var terrainWeight);

                definitions[key] = new CollisionDefinition(
                    key,
                    string.IsNullOrWhiteSpace(name) ? key : name,
                    string.IsNullOrWhiteSpace(kind) ? "Tile" : kind,
                    Math.Max(0, x),
                    Math.Max(0, y),
                    Math.Max(1, width),
                    Math.Max(1, height),
                    collision.EnumerateArray()
                        .Select(ParseCollisionState)
                        .ToArray(),
                    terrainKey,
                    NormalizeTerrainRole(terrainRole),
                    NormalizeTerrainDirection(terrainDirection),
                    terrainAgainst,
                    Math.Max(1, terrainWeight));
            }

            return (
                definitions,
                BuildTerrainIndex(terrains.Values, definitions.Values),
                BuildBrushes(document.RootElement, definitions),
                imageUrl,
                tileSize);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not load authoritative village collision definitions.");
            return (EmptyDefinitions.Instance, EmptyTerrainIndex.Instance, EmptyBrushes.Instance, string.Empty, 16);
        }
    }

    private static IReadOnlyDictionary<string, BrushDefinition> BuildBrushes(
        JsonElement root,
        IReadOnlyDictionary<string, CollisionDefinition> definitions)
    {
        var brushes = new Dictionary<string, BrushDefinition>(StringComparer.Ordinal);
        if (!root.TryGetProperty("brushes", out var rawBrushes) ||
            rawBrushes.ValueKind != JsonValueKind.Array)
        {
            return brushes;
        }

        foreach (var item in rawBrushes.EnumerateArray())
        {
            if (!TryGetString(item, "Key", "key", out var key) ||
                !TryGetInt(item, "Size", "size", out var size) ||
                size <= 0 || size > 32 ||
                !TryGetProperty(item, "Cells", "cells", out var rawCells) ||
                rawCells.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            TryGetString(item, "Name", "name", out var name);
            var cells = new List<BrushCellDefinition>();
            foreach (var rawCell in rawCells.EnumerateArray().Take(size * size))
            {
                TryGetString(rawCell, "TileKey", "tileKey", out var definitionKey);
                TryGetInt(rawCell, "Strength", "strength", out var strength);
                TryGetInt(rawCell, "Weight", "weight", out var weight);
                cells.Add(new BrushCellDefinition(
                    definitions.ContainsKey(definitionKey) ? definitionKey : string.Empty,
                    Math.Max(1, strength),
                    Math.Max(1, weight)));
            }

            while (cells.Count < size * size)
                cells.Add(new BrushCellDefinition(string.Empty, 1, 1));

            if (cells.All(x => x.DefinitionKey.Length == 0))
                continue;

            brushes[key] = new BrushDefinition(
                key,
                string.IsNullOrWhiteSpace(name) ? key : name,
                size,
                cells);
        }

        return brushes;
    }

    private static bool TryGetString(
        JsonElement element,
        string firstName,
        string secondName,
        out string value)
    {
        value = string.Empty;
        if (!TryGetProperty(element, firstName, secondName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return value.Length > 0;
    }

    private static bool TryGetInt(
        JsonElement element,
        string firstName,
        string secondName,
        out int value)
    {
        value = 0;
        return TryGetProperty(element, firstName, secondName, out var property) &&
               property.TryGetInt32(out value);
    }

    private static bool TryGetProperty(
        JsonElement element,
        string firstName,
        string secondName,
        out JsonElement value) =>
        element.TryGetProperty(firstName, out value) ||
        element.TryGetProperty(secondName, out value);

    private static IReadOnlyDictionary<string, TerrainIndexEntry> BuildTerrainIndex(
        IEnumerable<TerrainDefinition> terrains,
        IEnumerable<CollisionDefinition> definitions)
    {
        var index = new Dictionary<string, TerrainIndexEntry>(StringComparer.Ordinal);

        TerrainIndexEntry EnsureEntry(string key)
        {
            if (!index.TryGetValue(key, out var entry))
            {
                entry = new TerrainIndexEntry(new TerrainDefinition(key, key, 0));
                index[key] = entry;
            }

            return entry;
        }

        foreach (var terrain in terrains)
            EnsureEntry(terrain.Key).Terrain = terrain;

        foreach (var definition in definitions)
        {
            if (string.IsNullOrWhiteSpace(definition.TerrainKey))
                continue;

            var entry = EnsureEntry(definition.TerrainKey);
            if (definition.TerrainRole == "Base")
            {
                entry.BaseTiles.Add(definition);
                continue;
            }

            var validDirection = definition.TerrainRole == "Edge"
                ? definition.TerrainDirection is "N" or "E" or "S" or "W"
                : definition.TerrainDirection is "NE" or "SE" or "SW" or "NW";
            if (!validDirection)
                continue;

            if (!entry.Transitions.TryGetValue(definition.TerrainAgainst, out var set))
            {
                set = new TerrainTransitionSet();
                entry.Transitions[definition.TerrainAgainst] = set;
            }

            var bucket = definition.TerrainRole switch
            {
                "Edge" => set.Edges,
                "Corner" => set.Corners,
                _ => set.Inners,
            };
            if (!bucket.TryGetValue(definition.TerrainDirection, out var candidates))
            {
                candidates = [];
                bucket[definition.TerrainDirection] = candidates;
            }
            candidates.Add(definition);
        }

        return index;
    }

    private static CollisionDefinition? ResolveTerrainCell(
        string terrainKey,
        Func<int, int, string> getTerrainAt,
        int width,
        int height,
        int x,
        int y,
        IReadOnlyDictionary<string, TerrainIndexEntry> index)
    {
        if (x < 0 || y < 0 || x >= width || y >= height ||
            !index.TryGetValue(terrainKey, out var entry))
        {
            return null;
        }

        var baseTile = PickWeighted(entry.BaseTiles, x, y);
        var foreign = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["N"] = false, ["E"] = false, ["S"] = false, ["W"] = false,
            ["NE"] = false, ["SE"] = false, ["SW"] = false, ["NW"] = false,
        };
        var votes = new Dictionary<string, int>(StringComparer.Ordinal);
        var offsets = new (string Direction, int X, int Y, bool Cardinal)[]
        {
            ("N", 0, -1, true), ("E", 1, 0, true),
            ("S", 0, 1, true), ("W", -1, 0, true),
            ("NE", 1, -1, false), ("SE", 1, 1, false),
            ("SW", -1, 1, false), ("NW", -1, -1, false),
        };

        foreach (var offset in offsets)
        {
            var neighborX = x + offset.X;
            var neighborY = y + offset.Y;
            if (neighborX < 0 || neighborY < 0 || neighborX >= width || neighborY >= height)
                continue;

            var neighborKey = getTerrainAt(neighborX, neighborY);
            if (!BlendsToward(index, entry, neighborKey))
                continue;

            foreign[offset.Direction] = true;
            votes[neighborKey] = votes.GetValueOrDefault(neighborKey) + (offset.Cardinal ? 2 : 1);
        }

        var dominantAgainst = votes
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => x.Key)
            .FirstOrDefault();
        if (dominantAgainst is null || GetTransitionSet(entry, dominantAgainst) is not { } set)
            return baseTile;

        CollisionDefinition? Pick(
            Dictionary<string, List<CollisionDefinition>> choices,
            string direction) =>
            choices.TryGetValue(direction, out var candidates)
                ? PickWeighted(candidates, x, y)
                : null;

        var foreignCardinals = new[] { "N", "E", "S", "W" }
            .Where(direction => foreign[direction])
            .ToArray();
        if (foreignCardinals.Length == 0)
        {
            foreach (var direction in new[] { "NE", "SE", "SW", "NW" })
            {
                if (foreign[direction] && Pick(set.Inners, direction) is { } inner)
                    return inner;
            }

            return baseTile;
        }

        if (foreignCardinals.Length == 2)
        {
            var pair = string.Concat(foreignCardinals);
            var cornerDirection = pair switch
            {
                "NE" => "NE",
                "ES" => "SE",
                "SW" => "SW",
                "NW" => "NW",
                _ => string.Empty,
            };
            if (cornerDirection.Length > 0 && Pick(set.Corners, cornerDirection) is { } corner)
                return corner;
        }

        foreach (var direction in foreignCardinals)
        {
            if (Pick(set.Edges, direction) is { } edge)
                return edge;
        }

        return baseTile;
    }

    private static bool BlendsToward(
        IReadOnlyDictionary<string, TerrainIndexEntry> index,
        TerrainIndexEntry entry,
        string neighborKey)
    {
        if (string.IsNullOrWhiteSpace(neighborKey) || neighborKey == entry.Terrain.Key ||
            GetTransitionSet(entry, neighborKey) is null)
        {
            return false;
        }

        if (!index.TryGetValue(neighborKey, out var neighbor) ||
            GetTransitionSet(neighbor, entry.Terrain.Key) is null)
        {
            return true;
        }

        var selfSpecific = entry.Transitions.ContainsKey(neighborKey);
        var neighborSpecific = neighbor.Transitions.ContainsKey(entry.Terrain.Key);
        if (selfSpecific != neighborSpecific)
            return selfSpecific;
        if (entry.Terrain.Priority != neighbor.Terrain.Priority)
            return entry.Terrain.Priority > neighbor.Terrain.Priority;
        return string.CompareOrdinal(entry.Terrain.Key, neighbor.Terrain.Key) < 0;
    }

    private static TerrainTransitionSet? GetTransitionSet(TerrainIndexEntry entry, string against) =>
        entry.Transitions.GetValueOrDefault(against) ??
        entry.Transitions.GetValueOrDefault(string.Empty);

    private static CollisionDefinition? PickWeighted(
        IReadOnlyCollection<CollisionDefinition> tiles,
        int x,
        int y)
    {
        if (tiles.Count == 0)
            return null;
        if (tiles.Count == 1)
            return tiles.First();

        var totalWeight = tiles.Sum(tile => tile.TerrainWeight);
        var remaining = (int)(HashCell(x, y) % (uint)totalWeight);
        foreach (var tile in tiles)
        {
            remaining -= tile.TerrainWeight;
            if (remaining < 0)
                return tile;
        }

        return tiles.Last();
    }

    private static uint HashCell(int x, int y)
    {
        unchecked
        {
            var hash = x * 374761393 + y * 668265263;
            hash = (hash ^ (int)((uint)hash >> 13)) * 1274126177;
            return (uint)(hash ^ (int)((uint)hash >> 16));
        }
    }

    private static string NormalizeTerrainDirection(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "n" or "north" or "up" => "N",
            "e" or "east" or "right" => "E",
            "s" or "south" or "down" => "S",
            "w" or "west" or "left" => "W",
            "ne" or "northeast" or "upright" => "NE",
            "se" or "southeast" or "downright" => "SE",
            "sw" or "southwest" or "downleft" => "SW",
            "nw" or "northwest" or "upleft" => "NW",
            _ => "None",
        };

    private static string NormalizeTerrainRole(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "edge" => "Edge",
            "corner" or "outercorner" => "Corner",
            "inner" or "innercorner" => "InnerCorner",
            _ => "Base",
        };

    private static string ParseCollisionState(JsonElement cell) => cell.ValueKind switch
    {
        JsonValueKind.True => VillageCollisionState.Solid,
        JsonValueKind.False or JsonValueKind.Null => VillageCollisionState.Empty,
        JsonValueKind.String => VillageCollisionState.Normalize(cell.GetString()),
        JsonValueKind.Number when cell.TryGetInt32(out var value) =>
            value == 0 ? VillageCollisionState.Empty : VillageCollisionState.Solid,
        _ => VillageCollisionState.Solid,
    };

    private static IReadOnlyList<(int X, int Y)> GetDoorOffsets(
        CollisionDefinition definition,
        int footprintWidth,
        int footprintHeight)
    {
        var result = new List<(int X, int Y)>();
        var originY = Math.Max(1, footprintHeight) - definition.Height;
        var cellCount = Math.Min(
            definition.CollisionStates.Length,
            definition.Width * definition.Height);
        for (var index = 0; index < cellCount; index++)
        {
            if (!VillageCollisionState.IsDoor(definition.CollisionStates[index]))
                continue;

            var x = index % definition.Width;
            var y = originY + index / definition.Width;
            if (x >= 0 && x < footprintWidth && y >= 0 && y < footprintHeight)
                result.Add((x, y));
        }

        return result;
    }

    internal void SetMapForTesting(
        long planetId,
        long mapId,
        int width = 64,
        int height = 64,
        long? parentBuildingId = null,
        IEnumerable<(int X, int Y)>? blocked = null)
    {
        var map = new VillageCollisionMap(
            width,
            height,
            parentBuildingId,
            blocked?.Select(x => VillageCollisionMap.TileKey(x.X, x.Y)).ToHashSet() ?? []);
        _maps[(planetId, mapId)] = new Lazy<Task<VillageCollisionMap?>>(
            () => Task.FromResult<VillageCollisionMap?>(map));
    }

    internal VillageCollisionMap BuildMapForTesting(
        Valour.Database.VillageMap map,
        IEnumerable<Valour.Database.VillageObject>? objects = null,
        IEnumerable<Valour.Database.VillageBuilding>? buildings = null,
        IEnumerable<Valour.Database.VillageMapChunk>? chunks = null) =>
        VillageCollisionMap.Build(
            map,
            objects ?? [],
            buildings ?? [],
            chunks ?? [],
            _defaultDefinitions,
            _logger);

    internal sealed record CollisionDefinition(
        string Key,
        string Name,
        string Kind,
        int X,
        int Y,
        int Width,
        int Height,
        string[] CollisionStates,
        string TerrainKey,
        string TerrainRole,
        string TerrainDirection,
        string TerrainAgainst,
        int TerrainWeight)
    {
        public bool BlocksMovement => CollisionStates.Any(VillageCollisionState.BlocksMovement);
        public bool HasDoors => CollisionStates.Any(VillageCollisionState.IsDoor);
    }

    internal sealed record TerrainDefinition(string Key, string Name, int Priority);

    internal sealed record TerrainCatalogDefinition(
        string Key,
        string Name,
        CollisionDefinition Preview);

    internal sealed record BrushDefinition(
        string Key,
        string Name,
        int Size,
        IReadOnlyList<BrushCellDefinition> Cells);

    internal sealed record BrushCellDefinition(
        string DefinitionKey,
        int Strength,
        int Weight);

    private sealed class TerrainIndexEntry(TerrainDefinition terrain)
    {
        public TerrainDefinition Terrain { get; set; } = terrain;
        public List<CollisionDefinition> BaseTiles { get; } = [];
        public Dictionary<string, TerrainTransitionSet> Transitions { get; } = new(StringComparer.Ordinal);
    }

    private sealed class TerrainTransitionSet
    {
        public Dictionary<string, List<CollisionDefinition>> Edges { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, List<CollisionDefinition>> Corners { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, List<CollisionDefinition>> Inners { get; } = new(StringComparer.Ordinal);
    }

    private sealed class EmptyDefinitions : Dictionary<string, CollisionDefinition>
    {
        public static readonly EmptyDefinitions Instance = new();
        private EmptyDefinitions() : base(StringComparer.Ordinal) { }
    }

    private sealed class EmptyTerrainIndex : Dictionary<string, TerrainIndexEntry>
    {
        public static readonly EmptyTerrainIndex Instance = new();
        private EmptyTerrainIndex() : base(StringComparer.Ordinal) { }
    }

    private sealed class EmptyBrushes : Dictionary<string, BrushDefinition>
    {
        public static readonly EmptyBrushes Instance = new();
        private EmptyBrushes() : base(StringComparer.Ordinal) { }
    }

    internal sealed class VillageCollisionMap
    {
        private readonly HashSet<long> _blocked;

        public int Width { get; }
        public int Height { get; }
        public long? ParentBuildingId { get; }

        internal VillageCollisionMap(
            int width,
            int height,
            long? parentBuildingId,
            HashSet<long> blocked)
        {
            Width = width;
            Height = height;
            ParentBuildingId = parentBuildingId;
            _blocked = blocked;
        }

        public bool IsWalkable(int x, int y) =>
            x >= 0 &&
            y >= 0 &&
            x < Width &&
            y < Height &&
            !_blocked.Contains(TileKey(x, y));

        internal static VillageCollisionMap Build(
            Valour.Database.VillageMap map,
            IEnumerable<Valour.Database.VillageObject> objects,
            IEnumerable<Valour.Database.VillageBuilding> buildings,
            IEnumerable<Valour.Database.VillageMapChunk> chunks,
            IReadOnlyDictionary<string, CollisionDefinition> definitions,
            ILogger logger)
        {
            var blocked = new HashSet<long>();

            foreach (var item in objects)
            {
                if (!item.BlocksMovement)
                    continue;

                var footprint = VillageObjectGeometry.GetFootprint(item.DefinitionKey);
                if (item.DefinitionKey.StartsWith("buildings.", StringComparison.OrdinalIgnoreCase))
                {
                    // Building facades extend far above their ground footprint.
                    // Blocking the full opaque sprite would create invisible
                    // walls behind the structure instead of a compact base.
                    // Authored door states then carve reachable entrances out
                    // of that base without losing their semantic meaning.
                    AddRect(blocked, item.X, item.Y, footprint.Width, footprint.Height);
                    if (definitions.TryGetValue(item.DefinitionKey, out var buildingDefinition) &&
                        buildingDefinition.HasDoors)
                    {
                        var originY = item.Y + Math.Max(1, footprint.Height) - buildingDefinition.Height;
                        var cellCount = Math.Min(
                            buildingDefinition.CollisionStates.Length,
                            buildingDefinition.Width * buildingDefinition.Height);
                        for (var index = 0; index < cellCount; index++)
                        {
                            if (!VillageCollisionState.IsDoor(buildingDefinition.CollisionStates[index]))
                                continue;

                            var doorX = item.X + index % buildingDefinition.Width;
                            var doorY = originY + index / buildingDefinition.Width;
                            if (doorX >= item.X && doorX < item.X + footprint.Width &&
                                doorY >= item.Y && doorY < item.Y + footprint.Height)
                            {
                                blocked.Remove(TileKey(doorX, doorY));
                            }
                        }
                    }
                }
                else if (definitions.TryGetValue(item.DefinitionKey, out var definition) &&
                    definition.BlocksMovement)
                {
                    var originY = item.Y + Math.Max(1, footprint.Height) - definition.Height;
                    var cellCount = Math.Min(
                        definition.CollisionStates.Length,
                        definition.Width * definition.Height);
                    for (var index = 0; index < cellCount; index++)
                    {
                        if (VillageCollisionState.BlocksMovement(definition.CollisionStates[index]))
                        {
                            blocked.Add(TileKey(
                                item.X + index % definition.Width,
                                originY + index / definition.Width));
                        }
                    }
                }
                else
                {
                    AddRect(blocked, item.X, item.Y, footprint.Width, footprint.Height);
                }
            }

            foreach (var building in buildings)
            {
                AddRect(blocked, building.X, building.Y, building.Width, building.Height);

                var doorOffsets = !string.IsNullOrWhiteSpace(building.SpriteKey) &&
                                  definitions.TryGetValue(building.SpriteKey, out var definition)
                    ? GetDoorOffsets(definition, building.Width, building.Height)
                    : [];
                if (doorOffsets.Count == 0)
                {
                    // Legacy/community buildings may not have semantic states.
                    blocked.Remove(TileKey(building.DoorX, building.DoorY));
                    continue;
                }

                foreach (var door in doorOffsets)
                    blocked.Remove(TileKey(building.X + door.X, building.Y + door.Y));
            }

            foreach (var chunk in chunks)
                AddChunkCollision(blocked, chunk, logger);

            return new VillageCollisionMap(
                map.Width,
                map.Height,
                map.ParentBuildingId,
                blocked);
        }

        private static void AddChunkCollision(
            HashSet<long> blocked,
            Valour.Database.VillageMapChunk chunk,
            ILogger logger)
        {
            if (string.IsNullOrWhiteSpace(chunk.CollisionData))
                return;

            try
            {
                using var document = JsonDocument.Parse(chunk.CollisionData);
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object &&
                    (root.TryGetProperty("blocked", out var nested) ||
                     root.TryGetProperty("Blocked", out nested)))
                {
                    root = nested;
                }

                if (root.ValueKind != JsonValueKind.Array)
                    throw new JsonException("Collision data must be an array or contain a blocked array.");

                var values = root.EnumerateArray().ToArray();
                if (values.All(x => x.ValueKind is JsonValueKind.True or JsonValueKind.False))
                {
                    if (values.Length != ChunkSize * ChunkSize)
                        throw new JsonException($"A boolean collision mask must contain {ChunkSize * ChunkSize} cells.");

                    for (var index = 0; index < values.Length; index++)
                    {
                        if (values[index].GetBoolean())
                            AddChunkIndex(blocked, chunk.ChunkX, chunk.ChunkY, index);
                    }
                }
                else
                {
                    foreach (var value in values)
                    {
                        if (!value.TryGetInt32(out var index) || index < 0 || index >= ChunkSize * ChunkSize)
                            throw new JsonException("Blocked tile indices must be integers from 0 through 1023.");

                        AddChunkIndex(blocked, chunk.ChunkX, chunk.ChunkY, index);
                    }
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                // A malformed blocker must never turn into a fully walkable
                // chunk. Reject the chunk until its authored data is repaired.
                AddRect(
                    blocked,
                    chunk.ChunkX * ChunkSize,
                    chunk.ChunkY * ChunkSize,
                    ChunkSize,
                    ChunkSize);
                logger.LogWarning(
                    ex,
                    "Village collision data for chunk {ChunkId} is invalid; the chunk was blocked fail-closed.",
                    chunk.Id);
            }
        }

        private static void AddChunkIndex(HashSet<long> blocked, int chunkX, int chunkY, int index) =>
            blocked.Add(TileKey(
                chunkX * ChunkSize + index % ChunkSize,
                chunkY * ChunkSize + index / ChunkSize));

        private static void AddRect(HashSet<long> blocked, int x, int y, int width, int height)
        {
            for (var tileY = y; tileY < y + Math.Max(0, height); tileY++)
            {
                for (var tileX = x; tileX < x + Math.Max(0, width); tileX++)
                    blocked.Add(TileKey(tileX, tileY));
            }
        }

        internal static long TileKey(int x, int y) =>
            ((long)x << 32) | (uint)y;
    }
}
