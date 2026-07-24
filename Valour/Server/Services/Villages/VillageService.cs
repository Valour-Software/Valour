using Valour.Shared.Models;
using Valour.Shared.Villages;
using PlanetModel = Valour.Server.Models.Planet;
using ChannelModel = Valour.Server.Models.Channel;

namespace Valour.Server.Services.Villages;

public class VillageService
{
    public VillagePocScene BuildProofOfConceptScene(
        PlanetModel planet,
        IEnumerable<ChannelModel> channels,
        long userId)
    {
        var primaryChat = channels.FirstOrDefault(x => x.IsDefault) ??
                          channels.FirstOrDefault(x => x.ChannelType == ChannelTypeEnum.PlanetChat);

        var voiceChannel = channels.FirstOrDefault(x =>
            x.ChannelType == ChannelTypeEnum.PlanetVoice || x.ChannelType == ChannelTypeEnum.PlanetVideo);

        var outdoorMap = new VillagePocMap
        {
            Id = 1,
            Name = $"{planet.Name} Square",
            MapKind = "Outdoor",
            Width = 22,
            Height = 16,
            TileSize = 32,
            BackgroundColor = "#9fcf81",
            AccentColor = "#6da05f",
            BaseTileTextureUrl = "/_content/Valour.Client/media/villages/default-tileset/terrain/grass-base-32.png",
            SpawnTile = new VillagePocPoint { X = 10, Y = 11 },
            Plots =
            {
                new VillagePocPlot { Id = 1, Name = "Town Center", X = 2, Y = 2, Width = 7, Height = 5 },
                new VillagePocPlot { Id = 2, Name = "Creator Row", X = 12, Y = 2, Width = 7, Height = 5 },
                new VillagePocPlot { Id = 3, Name = "Workshop Lane", X = 5, Y = 9, Width = 12, Height = 5 }
            },
            Decorations =
            {
                new VillagePocDecoration
                {
                    Kind = "Path",
                    X = 0,
                    Y = 6,
                    Width = 22,
                    Height = 2,
                    Color = "#c9b387",
                    TextureUrl = "/_content/Valour.Client/media/villages/default-tileset/terrain/stone-path-base-32.png"
                },
                new VillagePocDecoration { Kind = "Tree", X = 1, Y = 1, Width = 1, Height = 1, Color = "#3a6b3f", BlocksMovement = true },
                new VillagePocDecoration { Kind = "Tree", X = 19, Y = 2, Width = 1, Height = 1, Color = "#3a6b3f", BlocksMovement = true },
                new VillagePocDecoration { Kind = "Fountain", X = 10, Y = 6, Width = 2, Height = 2, Color = "#77b5d9", BlocksMovement = true }
            },
            Buildings =
            {
                new VillagePocBuilding
                {
                    Id = 101,
                    Name = "Town Hall",
                    X = 3,
                    Y = 3,
                    Width = 4,
                    Height = 3,
                    Color = "#d7c29d",
                    RoofColor = "#8d6049",
                    Hint = "Main social hub for the community.",
                    InteriorMapId = 1001,
                    ChannelId = primaryChat?.Id,
                    ChannelName = primaryChat is null ? null : "Primary Chat",
                    EntranceTile = new VillagePocPoint { X = 5, Y = 5 },
                    CollisionRects =
                    {
                        new VillagePocRect { X = 3, Y = 3, Width = 4, Height = 2 }
                    }
                },
                new VillagePocBuilding
                {
                    Id = 102,
                    Name = "Voice Lounge",
                    X = 13,
                    Y = 3,
                    Width = 4,
                    Height = 3,
                    Color = "#d4b6e7",
                    RoofColor = "#80578f",
                    Hint = "A building mapped to a live voice channel.",
                    InteriorMapId = 1002,
                    ChannelId = voiceChannel?.Id,
                    ChannelName = voiceChannel?.Name,
                    EntranceTile = new VillagePocPoint { X = 15, Y = 5 },
                    CollisionRects =
                    {
                        new VillagePocRect { X = 13, Y = 3, Width = 4, Height = 2 }
                    }
                },
                new VillagePocBuilding
                {
                    Id = 103,
                    Name = "Builder Cottage",
                    X = 9,
                    Y = 10,
                    Width = 4,
                    Height = 3,
                    Color = "#d8a76c",
                    RoofColor = "#915b2f",
                    Hint = "Interior decorating, furniture, and future plot editing.",
                    EntranceTile = new VillagePocPoint { X = 11, Y = 12 },
                    CollisionRects =
                    {
                        new VillagePocRect { X = 9, Y = 10, Width = 4, Height = 2 }
                    }
                }
            },
            BlockedTiles =
            {
                new VillagePocRect { X = 1, Y = 1, Width = 1, Height = 1 },
                new VillagePocRect { X = 19, Y = 2, Width = 1, Height = 1 },
                new VillagePocRect { X = 10, Y = 6, Width = 2, Height = 2 },
                new VillagePocRect { X = 3, Y = 3, Width = 4, Height = 2 },
                new VillagePocRect { X = 13, Y = 3, Width = 4, Height = 2 },
                new VillagePocRect { X = 9, Y = 10, Width = 4, Height = 2 }
            },
            Portals =
            {
                new VillagePocPortal
                {
                    Kind = "Door",
                    X = 5,
                    Y = 5,
                    TargetMapId = 1001,
                    TargetX = 7,
                    TargetY = 9,
                    BuildingId = 101,
                    Color = "#fff2a8"
                },
                new VillagePocPortal
                {
                    Kind = "Door",
                    X = 15,
                    Y = 5,
                    TargetMapId = 1002,
                    TargetX = 7,
                    TargetY = 9,
                    BuildingId = 102,
                    Color = "#fff2a8"
                }
            }
        };

        var townHallInterior = new VillagePocMap
        {
            Id = 1001,
            Name = "Town Hall Interior",
            MapKind = "Interior",
            Width = 14,
            Height = 10,
            TileSize = 32,
            BackgroundColor = "#d8c9a8",
            AccentColor = "#8d6049",
            BaseTileTextureUrl = "/_content/Valour.Client/media/villages/default-tileset/terrain/grass-base-32.png",
            ParentBuildingId = 101,
            SpawnTile = new VillagePocPoint { X = 7, Y = 9 },
            Decorations =
            {
                new VillagePocDecoration { Kind = "Counter", X = 4, Y = 2, Width = 6, Height = 1, Color = "#8d6049", BlocksMovement = true },
                new VillagePocDecoration { Kind = "Plant", X = 2, Y = 2, Width = 1, Height = 1, Color = "#4e7a43", BlocksMovement = true },
                new VillagePocDecoration { Kind = "Plant", X = 11, Y = 2, Width = 1, Height = 1, Color = "#4e7a43", BlocksMovement = true },
                new VillagePocDecoration { Kind = "Table", X = 5, Y = 6, Width = 3, Height = 2, Color = "#a87449", BlocksMovement = true }
            },
            BlockedTiles =
            {
                new VillagePocRect { X = 4, Y = 2, Width = 6, Height = 1 },
                new VillagePocRect { X = 2, Y = 2, Width = 1, Height = 1 },
                new VillagePocRect { X = 11, Y = 2, Width = 1, Height = 1 },
                new VillagePocRect { X = 5, Y = 6, Width = 3, Height = 2 }
            },
            Portals =
            {
                new VillagePocPortal
                {
                    Kind = "Exit",
                    X = 7,
                    Y = 9,
                    TargetMapId = outdoorMap.Id,
                    TargetX = 5,
                    TargetY = 5,
                    BuildingId = 101,
                    Color = "#c9f0ff"
                }
            }
        };

        var voiceLoungeInterior = new VillagePocMap
        {
            Id = 1002,
            Name = "Voice Lounge Interior",
            MapKind = "Interior",
            Width = 14,
            Height = 10,
            TileSize = 32,
            BackgroundColor = "#b8a6ca",
            AccentColor = "#80578f",
            BaseTileTextureUrl = "/_content/Valour.Client/media/villages/default-tileset/terrain/grass-base-32.png",
            ParentBuildingId = 102,
            SpawnTile = new VillagePocPoint { X = 7, Y = 9 },
            Decorations =
            {
                new VillagePocDecoration { Kind = "Sofa", X = 3, Y = 5, Width = 4, Height = 2, Color = "#6f4f80", BlocksMovement = true },
                new VillagePocDecoration { Kind = "Sofa", X = 8, Y = 5, Width = 3, Height = 2, Color = "#6f4f80", BlocksMovement = true },
                new VillagePocDecoration { Kind = "CoffeeTable", X = 6, Y = 4, Width = 2, Height = 1, Color = "#9d7c57", BlocksMovement = true },
                new VillagePocDecoration { Kind = "Lamp", X = 2, Y = 2, Width = 1, Height = 1, Color = "#f2d36c", BlocksMovement = true }
            },
            BlockedTiles =
            {
                new VillagePocRect { X = 3, Y = 5, Width = 4, Height = 2 },
                new VillagePocRect { X = 8, Y = 5, Width = 3, Height = 2 },
                new VillagePocRect { X = 6, Y = 4, Width = 2, Height = 1 },
                new VillagePocRect { X = 2, Y = 2, Width = 1, Height = 1 }
            },
            Portals =
            {
                new VillagePocPortal
                {
                    Kind = "Exit",
                    X = 7,
                    Y = 9,
                    TargetMapId = outdoorMap.Id,
                    TargetX = 15,
                    TargetY = 5,
                    BuildingId = 102,
                    Color = "#c9f0ff"
                }
            }
        };

        return new VillagePocScene
        {
            PlanetId = planet.Id,
            PlanetName = planet.Name,
            Title = $"{planet.Name} Village",
            Subtitle = "Canvas-powered town square with enterable interiors",
            StartingMapId = outdoorMap.Id,
            Maps = { outdoorMap, townHallInterior, voiceLoungeInterior },
            Characters =
            {
                new VillagePocCharacter
                {
                    UserId = userId,
                    Name = "You",
                    MapId = outdoorMap.Id,
                    X = 10,
                    Y = 11,
                    IsLocalPlayer = true,
                    BodyColor = "#f2d0b8",
                    HairColor = "#4c3327",
                    TopColor = "#2d73d5",
                    BottomColor = "#37485d"
                },
                new VillagePocCharacter
                {
                    UserId = 0,
                    Name = "Builder Bot",
                    MapId = outdoorMap.Id,
                    X = 5,
                    Y = 10,
                    BodyColor = "#f1c7aa",
                    HairColor = "#2f2a22",
                    TopColor = "#52a363",
                    BottomColor = "#46513a"
                },
                new VillagePocCharacter
                {
                    UserId = -1,
                    Name = "Host",
                    MapId = townHallInterior.Id,
                    X = 7,
                    Y = 5,
                    BodyColor = "#f1c7aa",
                    HairColor = "#5b2f25",
                    TopColor = "#d45454",
                    BottomColor = "#5e3b3b"
                }
            }
        };
    }
}
