using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Valour.Database.Context;

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

    public VillageCollisionService(
        IServiceScopeFactory scopeFactory,
        ILogger<VillageCollisionService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _defaultDefinitions = LoadDefinitions();
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

    private IReadOnlyDictionary<string, CollisionDefinition> LoadDefinitions()
    {
        try
        {
            using var stream = typeof(VillageCollisionService).Assembly
                .GetManifestResourceStream(DefaultTilesetResource);
            if (stream is null)
            {
                _logger.LogError("Embedded village tileset {Resource} was not found.", DefaultTilesetResource);
                return EmptyDefinitions.Instance;
            }

            using var document = JsonDocument.Parse(stream);
            var definitions = new Dictionary<string, CollisionDefinition>(StringComparer.Ordinal);

            if (!document.RootElement.TryGetProperty("definitions", out var rawDefinitions) ||
                rawDefinitions.ValueKind != JsonValueKind.Array)
            {
                return definitions;
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

                definitions[key] = new CollisionDefinition(
                    Math.Max(1, width),
                    Math.Max(1, height),
                    collision.EnumerateArray()
                        .Select(x => x.ValueKind == JsonValueKind.True)
                        .ToArray());
            }

            return definitions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not load authoritative village collision definitions.");
            return EmptyDefinitions.Instance;
        }
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

    internal sealed record CollisionDefinition(int Width, int Height, bool[] Collision);

    private sealed class EmptyDefinitions : Dictionary<string, CollisionDefinition>
    {
        public static readonly EmptyDefinitions Instance = new();
        private EmptyDefinitions() : base(StringComparer.Ordinal) { }
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
                if (definitions.TryGetValue(item.DefinitionKey, out var definition) &&
                    definition.Collision.Any(x => x))
                {
                    var originY = item.Y + Math.Max(1, footprint.Height) - definition.Height;
                    var cellCount = Math.Min(
                        definition.Collision.Length,
                        definition.Width * definition.Height);
                    for (var index = 0; index < cellCount; index++)
                    {
                        if (definition.Collision[index])
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
                AddRect(
                    blocked,
                    building.X,
                    building.Y,
                    building.Width,
                    Math.Max(1, building.Height - 1));

                // The authored door is the only walkable tile in the footprint.
                blocked.Remove(TileKey(building.DoorX, building.DoorY));
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
