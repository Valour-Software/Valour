using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;
using Valour.Database;
using Valour.Database.Context;
using Valour.Database.Seeds.Villages;
using Valour.Server.Database;
using Valour.Shared;
using Valour.Shared.Models;
using Valour.Shared.Models.Staff;
using Valour.Shared.Villages;
using VillageMap = Valour.Database.VillageMap;
using VillageBuilding = Valour.Database.VillageBuilding;
using VillagePlot = Valour.Database.VillagePlot;
using VillageObject = Valour.Database.VillageObject;
using VillageMapChunk = Valour.Database.VillageMapChunk;
using StaffAuditLog = Valour.Database.StaffAuditLog;
using ChannelModel = Valour.Server.Models.Channel;

namespace Valour.Server.Services.Villages;

public sealed class VillageTemplateService(ValourDb db, VillageCollisionService collision)
{
    private static readonly JsonSerializerOptions SnapshotJson = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    internal Task<VillageTemplate> GetSettingsAsync() => GetSettingsCoreAsync();

    private async Task<VillageTemplate> GetSettingsCoreAsync() =>
        await db.VillageTemplates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1) ?? new VillageTemplate();

    public async Task<VillageTemplateStatus> GetStatusAsync()
    {
        var settings = await GetSettingsAsync();
        return new VillageTemplateStatus
        {
            Revision = settings.Revision,
            ResetBeforeRevision = settings.ResetBeforeRevision,
            DraftPlanetId = settings.DraftPlanetId,
            DraftPlanetName = await db.Planets.Where(x => x.Id == settings.DraftPlanetId).Select(x => x.Name).FirstOrDefaultAsync(),
            DraftMapCount = await db.VillageMaps.CountAsync(x => x.PlanetId == settings.DraftPlanetId),
            PublishedMapCount = ReadSnapshot(settings).Maps.Count,
            PublishedAt = settings.PublishedAt,
            PublishedBy = await db.Users.Where(x => x.Id == settings.PublishedByUserId).Select(x => x.Name).FirstOrDefaultAsync(),
            IsBuiltIn = settings.PublishedAt is null && settings.PublishedByUserId is null
        };
    }

    public async Task<TaskResult<VillageTemplateStatus>> SelectDraftAsync(VillageTemplateDraftRequest request, long staffId)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        await LockSettingsAsync();
        var settings = await TrackedSettingsAsync();
        if (settings.Revision != request.ExpectedRevision)
            return TaskResult<VillageTemplateStatus>.FromFailure("The template changed. Refresh before choosing a draft.");
        if (!await db.VillageMaps.AnyAsync(x => x.PlanetId == request.PlanetId && x.MapType == VillageMapType.Outdoor))
            return TaskResult<VillageTemplateStatus>.FromFailure("Open that village first to create its maps.");
        settings.DraftPlanetId = request.PlanetId;
        AddAudit(staffId, StaffActionType.SelectVillageTemplateDraft, $"Selected village {request.PlanetId} as the shared template draft.");
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return TaskResult<VillageTemplateStatus>.FromData(await GetStatusAsync());
    }

    public async Task<TaskResult<VillageTemplateStatus>> PublishAsync(VillageTemplatePublishRequest request, long staffId)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        await LockSettingsAsync();
        var settings = await TrackedSettingsAsync();
        if (settings.Revision != request.ExpectedRevision || settings.DraftPlanetId != request.DraftPlanetId)
            return TaskResult<VillageTemplateStatus>.FromFailure("The template or draft changed. Refresh and review it before publishing.");

        var snapshot = await CaptureAsync(request.DraftPlanetId);
        var error = Validate(snapshot);
        if (error is not null)
            return TaskResult<VillageTemplateStatus>.FromFailure(error);

        settings.PublishedJson = JsonSerializer.Serialize(snapshot, SnapshotJson);
        settings.Revision++;
        if (request.ResetExisting) settings.ResetBeforeRevision = settings.Revision;
        settings.PublishedAt = DateTime.UtcNow;
        settings.PublishedByUserId = staffId;
        AddAudit(staffId, StaffActionType.PublishVillageTemplate,
            $"Published village template revision {settings.Revision} from {request.DraftPlanetId}; reset existing worlds: {request.ResetExisting}.");
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return TaskResult<VillageTemplateStatus>.FromData(await GetStatusAsync());
    }

    internal async Task CopyPublishedAsync(long planetId, IEnumerable<ChannelModel> channels, VillageTemplate settings)
    {
        var snapshot = ReadSnapshot(settings);
        var ids = snapshot.Maps.Select(x => x.Id).Concat(snapshot.Buildings.Select(x => x.Id))
            .Concat(snapshot.Plots.Select(x => x.Id)).ToDictionary(x => x, _ => IdManager.Generate());
        var channelList = channels.ToList();
        var hasCurrency = await db.Currencies.AnyAsync(x => x.PlanetId == planetId);
        foreach (var map in snapshot.Maps)
        {
            map.Id = ids[map.Id]; map.PlanetId = planetId; map.Version = 1;
            map.TemplateRevision = settings.Revision;
            map.ParentBuildingId = map.ParentBuildingId is { } parent ? ids[parent] : null;
            db.VillageMaps.Add(map);
        }
        foreach (var plot in snapshot.Plots)
        {
            plot.Id = ids[plot.Id]; plot.PlanetId = planetId; plot.MapId = ids[plot.MapId];
            plot.OwnerMemberId = null; plot.Price = hasCurrency ? plot.Price : 0;
            plot.SaleId = plot.ForSale ? Guid.NewGuid().ToString("N") : null;
            db.VillagePlots.Add(plot);
        }
        foreach (var building in snapshot.Buildings)
        {
            building.Id = ids[building.Id]; building.PlanetId = planetId; building.MapId = ids[building.MapId];
            building.InteriorMapId = building.InteriorMapId is { } interior ? ids[interior] : null;
            building.PlotId = building.PlotId is { } plot ? ids[plot] : null;
            building.OwnerMemberId = null; building.Price = hasCurrency ? building.Price : 0;
            building.SaleId = building.ForSale ? Guid.NewGuid().ToString("N") : null;
            building.ChannelId = building.ChannelId is { } oldChannel && snapshot.ChannelTypes.TryGetValue(oldChannel, out var type)
                ? channelList.OrderByDescending(x => x.IsDefault).FirstOrDefault(x => x.ChannelType == type)?.Id : null;
            db.VillageBuildings.Add(building);
        }
        foreach (var item in snapshot.Objects)
        {
            item.Id = IdManager.Generate(); item.PlanetId = planetId; item.MapId = ids[item.MapId]; item.OwnerMemberId = null;
            db.VillageObjects.Add(item);
        }
        foreach (var chunk in snapshot.Chunks)
        {
            chunk.Id = IdManager.Generate(); chunk.PlanetId = planetId; chunk.MapId = ids[chunk.MapId]; chunk.Version = 1;
            db.VillageMapChunks.Add(chunk);
        }
        await db.SaveChangesAsync();
    }

    private async Task<VillageTemplateSnapshot> CaptureAsync(long planetId)
    {
        var maps = await db.VillageMaps.AsNoTracking().Where(x => x.PlanetId == planetId).ToListAsync();
        var mapIds = maps.Select(x => x.Id).ToList();
        var snapshot = new VillageTemplateSnapshot
        {
            Maps = maps,
            Buildings = await db.VillageBuildings.AsNoTracking().Where(x => x.PlanetId == planetId && mapIds.Contains(x.MapId)).ToListAsync(),
            Plots = await db.VillagePlots.AsNoTracking().Where(x => x.PlanetId == planetId && mapIds.Contains(x.MapId)).ToListAsync(),
            Objects = await db.VillageObjects.AsNoTracking().Where(x => x.PlanetId == planetId && mapIds.Contains(x.MapId)).ToListAsync(),
            Chunks = await db.VillageMapChunks.AsNoTracking().Where(x => x.PlanetId == planetId && mapIds.Contains(x.MapId)).ToListAsync(),
            ChannelTypes = await db.Channels.AsNoTracking().Where(x => x.PlanetId == planetId).ToDictionaryAsync(x => x.Id, x => x.ChannelType)
        };
        foreach (var map in maps) map.Planet = null;
        foreach (var building in snapshot.Buildings) { building.Planet = null; building.OwnerMemberId = null; building.SaleId = null; }
        foreach (var plot in snapshot.Plots) { plot.Planet = null; plot.OwnerMemberId = null; plot.SaleId = null; }
        foreach (var item in snapshot.Objects) { item.Planet = null; item.OwnerMemberId = null; }
        foreach (var chunk in snapshot.Chunks) chunk.Planet = null;
        return snapshot;
    }

    internal string Validate(VillageTemplateSnapshot snapshot)
    {
        if (snapshot.Maps.Count is < 1 or > 100 || snapshot.Maps.Count(x => x.MapType == VillageMapType.Outdoor) != 1)
            return "A template needs exactly one outdoor map and at most 99 interiors.";
        if (snapshot.Objects.Count > 200000 || snapshot.Buildings.Count > 100 || snapshot.Plots.Count > 1000)
            return "This draft exceeds the template size limit.";
        var mapIds = snapshot.Maps.Select(x => x.Id).ToHashSet();
        var buildingIds = snapshot.Buildings.Select(x => x.Id).ToHashSet();
        foreach (var map in snapshot.Maps)
        {
            if (map.Width is < 4 or > 256 || map.Height is < 4 or > 256 ||
                map.TilesetKey != "exterior-tileset-0" || map.TileSize != 32)
                return $"{map.Name}: unsupported map size or tileset.";
            if (map.MapType == VillageMapType.Interior &&
                (map.ParentBuildingId is not { } parent || !buildingIds.Contains(parent) ||
                 !snapshot.Buildings.Any(x => x.Id == parent && x.InteriorMapId == map.Id)))
                return $"{map.Name}: this interior has no connected building.";
            var buildings = snapshot.Buildings.Where(x => x.MapId == map.Id).ToList();
            var objects = snapshot.Objects.Where(x => x.MapId == map.Id).ToList();
            foreach (var item in objects)
            {
                if (item.X < 0 || item.Y < 0 || item.X >= map.Width || item.Y >= map.Height)
                    return $"{map.Name}: an object sits outside the map.";
                if (VillageWallTopology.TryParseDefinitionKey(item.DefinitionKey, out var wallKey, out _))
                {
                    if (!collision.TryGetWallSet(map.TilesetKey, wallKey, out _)) return $"{map.Name}: unknown wall {wallKey}.";
                }
                else if (!collision.TryGetDefinition(map.TilesetKey, item.DefinitionKey, out _))
                    return $"{map.Name}: unknown furniture or floor {item.DefinitionKey}.";
            }
            var walkability = collision.BuildMapForTesting(map, objects, buildings,
                snapshot.Chunks.Where(x => x.MapId == map.Id).ToList());
            if (!walkability.IsWalkable(map.SpawnX, map.SpawnY))
                return $"{map.Name}: clear the arrival tile before publishing.";
            var visited = new HashSet<(int X, int Y)> { (map.SpawnX, map.SpawnY) };
            var queue = new Queue<(int X, int Y)>(); queue.Enqueue((map.SpawnX, map.SpawnY));
            while (queue.TryDequeue(out var point))
                foreach (var next in new[] { (point.X + 1, point.Y), (point.X - 1, point.Y), (point.X, point.Y + 1), (point.X, point.Y - 1) })
                    if (walkability.IsWalkable(next.Item1, next.Item2) && visited.Add(next)) queue.Enqueue(next);
            if (map.MapType == VillageMapType.Interior && !visited.Contains((map.SpawnX, map.SpawnY + 1)))
                return $"{map.Name}: the exit is blocked.";
            foreach (var building in buildings)
            {
                if (building.InteriorMapId is { } interior && !mapIds.Contains(interior)) return $"{building.Name}: missing interior.";
                if (!collision.TryGetDefinition(map.TilesetKey, building.SpriteKey, out var definition) || !building.SpriteKey.StartsWith("buildings.", StringComparison.Ordinal))
                    return $"{building.Name}: unknown building sprite.";
                if (!visited.Contains((building.DoorX, building.DoorY)))
                    return $"{building.Name}: its door cannot be reached from the arrival point.";
                if (building.PlotId is { } plot && !snapshot.Plots.Any(x => x.Id == plot && x.MapId == map.Id))
                    return $"{building.Name}: missing plot.";
            }
        }
        var reachableMaps = new HashSet<long> { snapshot.Maps.Single(x => x.MapType == VillageMapType.Outdoor).Id };
        for (var pass = 0; pass < snapshot.Maps.Count; pass++)
            foreach (var building in snapshot.Buildings)
                if (reachableMaps.Contains(building.MapId) && building.InteriorMapId is { } interior) reachableMaps.Add(interior);
        return reachableMaps.Count == snapshot.Maps.Count ? null : "Some interiors cannot be reached from outdoors.";
    }

    private static VillageTemplateSnapshot ReadSnapshot(VillageTemplate settings) =>
        JsonSerializer.Deserialize<VillageTemplateSnapshot>(settings.PublishedJson ?? VillageDefaultWorld.CurrentJson, SnapshotJson)
        ?? throw new InvalidOperationException("Published village template is empty.");

    private async Task LockSettingsAsync() =>
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(-71411921)");

    private async Task<VillageTemplate> TrackedSettingsAsync()
    {
        var settings = await db.VillageTemplates.SingleOrDefaultAsync(x => x.Id == 1);
        if (settings is not null) return settings;
        settings = new VillageTemplate(); db.VillageTemplates.Add(settings); return settings;
    }

    internal void AddAudit(long staffId, StaffActionType action, string reason) =>
        db.StaffAuditLogs.Add(new StaffAuditLog
        {
            Id = IdManager.Generate(), StaffUserId = staffId, ActionType = action,
            Reason = reason, TimeCreated = DateTime.UtcNow
        });
}

internal sealed class VillageTemplateSnapshot
{
    public List<VillageMap> Maps { get; set; } = [];
    public List<VillageBuilding> Buildings { get; set; } = [];
    public List<VillagePlot> Plots { get; set; } = [];
    public List<VillageObject> Objects { get; set; } = [];
    public List<VillageMapChunk> Chunks { get; set; } = [];
    public Dictionary<long, ChannelTypeEnum> ChannelTypes { get; set; } = [];
}
