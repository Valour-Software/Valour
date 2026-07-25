using Microsoft.EntityFrameworkCore;
using Valour.Database.Context;
using Valour.Server.Database;
using Valour.Shared.Models;
using Valour.Shared.Villages;
using ChannelModel = Valour.Server.Models.Channel;
using PlanetModel = Valour.Server.Models.Planet;
using PlanetMemberModel = Valour.Server.Models.PlanetMember;

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
    private readonly ValourDb _db;
    private readonly ILogger<VillageWorldService> _logger;

    public VillageWorldService(ValourDb db, ILogger<VillageWorldService> logger)
    {
        _db = db;
        _logger = logger;
    }

    private const string DefaultTileset = "exterior-tileset-0";
    private const string GrassTexture = "/_content/Valour.Client/media/villages/default-tileset/terrain/grass-base-32.png";

    /// <summary>
    /// Returns the planet's village, creating a starter world on first open.
    /// A planet that has villages enabled but no map yet would otherwise show an
    /// empty void, which reads as broken rather than as new.
    /// </summary>
    public async Task<VillagePocScene> GetOrCreateSceneAsync(
        PlanetModel planet,
        IEnumerable<ChannelModel> channels,
        PlanetMemberModel member)
    {
        var maps = await _db.VillageMaps
            .Where(x => x.PlanetId == planet.Id)
            .OrderBy(x => x.Id)
            .ToListAsync();

        if (maps.Count == 0)
        {
            await SeedWorldAsync(planet, channels);

            maps = await _db.VillageMaps
                .Where(x => x.PlanetId == planet.Id)
                .OrderBy(x => x.Id)
                .ToListAsync();
        }

        return await BuildSceneAsync(planet, maps, channels, member);
    }

    private async Task<VillagePocScene> BuildSceneAsync(
        PlanetModel planet,
        List<Valour.Database.VillageMap> maps,
        IEnumerable<ChannelModel> channels,
        PlanetMemberModel member)
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

        var channelLookup = channels.ToDictionary(x => x.Id, x => x.Name);
        var outdoor = maps.FirstOrDefault(x => x.MapType == VillageMapType.Outdoor) ?? maps[0];

        var scene = new VillagePocScene
        {
            PlanetId = planet.Id,
            PlanetName = planet.Name,
            Title = $"{planet.Name} Village",
            Subtitle = "A place to walk around, meet, and talk",
            StartingMapId = outdoor.Id,
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
                BaseTileTextureUrl = GrassTexture,
                ParentBuildingId = map.ParentBuildingId,
                SpawnTile = new VillagePocPoint { X = map.SpawnX, Y = map.SpawnY },
            };

            foreach (var plot in plots.Where(x => x.MapId == map.Id))
            {
                pocMap.Plots.Add(new VillagePocPlot
                {
                    Id = plot.Id,
                    Name = plot.OwnerMemberId is null && plot.ForSale
                        ? $"{plot.Name} (for sale)"
                        : plot.Name,
                    X = plot.X,
                    Y = plot.Y,
                    Width = plot.Width,
                    Height = plot.Height,
                });
            }

            foreach (var item in objects.Where(x => x.MapId == map.Id))
            {
                pocMap.Decorations.Add(new VillagePocDecoration
                {
                    Kind = item.DefinitionKey,
                    X = item.X,
                    Y = item.Y,
                    Width = 1,
                    Height = 1,
                    Color = "#4e7a43",
                    BlocksMovement = item.BlocksMovement,
                });
            }

            foreach (var building in mapBuildings)
            {
                channelLookup.TryGetValue(building.ChannelId ?? 0, out var channelName);

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
                    InteriorMapId = building.InteriorMapId,
                    ChannelId = building.ChannelId,
                    ChannelName = channelName,
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

    /// <summary>
    /// Creates the starter village: a square with three buildings, each with its
    /// own interior, wired to whatever channels the planet already has.
    /// </summary>
    private async Task SeedWorldAsync(PlanetModel planet, IEnumerable<ChannelModel> channels)
    {
        var channelList = channels.ToList();

        var primaryChat = channelList.FirstOrDefault(x => x.IsDefault)
            ?? channelList.FirstOrDefault(x => x.ChannelType == ChannelTypeEnum.PlanetChat);

        var voiceChannel = channelList.FirstOrDefault(x =>
            x.ChannelType is ChannelTypeEnum.PlanetVoice or ChannelTypeEnum.PlanetVideo);

        var outdoor = new Valour.Database.VillageMap
        {
            Id = IdManager.Generate(),
            PlanetId = planet.Id,
            MapType = VillageMapType.Outdoor,
            Name = $"{planet.Name} Square",
            Width = 32,
            Height = 24,
            TileSize = 32,
            SpawnX = 16,
            SpawnY = 18,
            TilesetKey = DefaultTileset,
            Version = 1,
        };

        _db.VillageMaps.Add(outdoor);

        var blueprints = new[]
        {
            new { Name = "Town Hall", Description = "The main hub for the community.", X = 4, Y = 4, W = 5, H = 4, ChannelId = primaryChat?.Id, Voice = VillageVoiceMode.LinkedChannel },
            new { Name = "Voice Lounge", Description = "Drop in and talk.", X = 20, Y = 4, W = 5, H = 4, ChannelId = voiceChannel?.Id, Voice = VillageVoiceMode.LinkedChannel },
            new { Name = "Workshop", Description = "A space to claim and make your own.", X = 12, Y = 13, W = 6, H = 4, ChannelId = (long?)null, Voice = VillageVoiceMode.None },
        };

        foreach (var blueprint in blueprints)
        {
            var interior = new Valour.Database.VillageMap
            {
                Id = IdManager.Generate(),
                PlanetId = planet.Id,
                MapType = VillageMapType.Interior,
                Name = $"{blueprint.Name} Interior",
                Width = 14,
                Height = 10,
                TileSize = 32,
                SpawnX = 7,
                SpawnY = 8,
                TilesetKey = DefaultTileset,
                Version = 1,
            };

            _db.VillageMaps.Add(interior);

            var building = new Valour.Database.VillageBuilding
            {
                Id = IdManager.Generate(),
                PlanetId = planet.Id,
                MapId = outdoor.Id,
                InteriorMapId = interior.Id,
                Name = blueprint.Name,
                Description = blueprint.Description,
                X = blueprint.X,
                Y = blueprint.Y,
                Width = blueprint.W,
                Height = blueprint.H,
                // Bottom-centre, so the door sits on the wall facing the square.
                DoorX = blueprint.X + (blueprint.W / 2),
                DoorY = blueprint.Y + blueprint.H - 1,
                SpriteKey = "buildings.house-medium",
                VoiceMode = blueprint.Voice,
                ChannelId = blueprint.ChannelId,
            };

            _db.VillageBuildings.Add(building);
            interior.ParentBuildingId = building.Id;

            _db.VillagePlots.Add(new Valour.Database.VillagePlot
            {
                Id = IdManager.Generate(),
                PlanetId = planet.Id,
                MapId = outdoor.Id,
                Name = $"{blueprint.Name} Plot",
                X = blueprint.X - 1,
                Y = blueprint.Y - 1,
                Width = blueprint.W + 2,
                Height = blueprint.H + 2,
                EditMode = VillageEditMode.Owner,
                // The workshop is offered up for someone to claim; the civic
                // buildings are not.
                ForSale = blueprint.ChannelId is null,
                Price = 0,
            });
        }

        await _db.SaveChangesAsync();

        _logger.LogInformation("Seeded starter village for planet {PlanetId}.", planet.Id);
    }
}
