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
    private readonly ValourDb _db;
    private readonly CoreHubService _hubService;
    private readonly ILogger<VillageWorldService> _logger;

    public VillageWorldService(ValourDb db, CoreHubService hubService, ILogger<VillageWorldService> logger)
    {
        _db = db;
        _hubService = hubService;
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
                BaseTileTextureUrl = map.MapType == VillageMapType.Outdoor
                    ? GrassTexture
                    : InteriorFloorTexture,
                TilesetKey = map.TilesetKey,
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
                var footprint = GetObjectFootprint(item.DefinitionKey);
                var decoration = new VillagePocDecoration
                {
                    Kind = item.DefinitionKey,
                    DefinitionKey = item.DefinitionKey,
                    X = item.X,
                    Y = item.Y,
                    Width = footprint.Width,
                    Height = footprint.Height,
                    ZIndex = item.ZIndex,
                    Color = "#4e7a43",
                    BlocksMovement = item.BlocksMovement,
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

    private static (int Width, int Height) GetObjectFootprint(string definitionKey) =>
        definitionKey switch
        {
            "small-tree" or
            "small-tree.with-grass" or
            "small-tree-planter" or
            "small-tree-planter.square" => (2, 1),
            "trees.large-tree" or
            "trees.large-tree.with-grass" or
            "trees.large-tree-planter" or
            "large-tree-planter.square" => (3, 1),
            "furniture.park-bench" => (2, 1),
            "decor.stone-fountain" => (2, 2),
            "garden.flowers.white" or
            "garden.flowers.pink" or
            "garden.flowers.red" or
            "garden.planter.white" or
            "garden.planter.yellow" or
            "garden.planter.pink" => (2, 1),
            "commerce.market-stall" => (2, 1),
            _ => (1, 1),
        };

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
