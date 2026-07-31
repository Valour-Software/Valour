using System.Text.Json.Serialization;

namespace Valour.Shared.Villages;

public class VillagePocScene
{
    [JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]
    public long PlanetId { get; set; }
    [JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]
    public long LocalMemberId { get; set; }

    /// <summary>
    /// Whether the local member holds ManageVillage on this planet. The client
    /// uses this to decide whether to offer management controls at all; the
    /// server re-checks the permission on every management request.
    /// </summary>
    public bool CanManageVillage { get; set; }
    public string PlanetName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string CurrencySymbol { get; set; } = string.Empty;
    public string CurrencyShortCode { get; set; } = string.Empty;
    public int CurrencyDecimalPlaces { get; set; }
    [JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]
    public long? DefaultChatChannelId { get; set; }
    public string? DefaultChatChannelName { get; set; }
    [JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]
    public long StartingMapId { get; set; }
    public List<VillagePocMap> Maps { get; set; } = new();
    public List<VillagePocCharacter> Characters { get; set; } = new();
    public string BuildCatalogImageUrl { get; set; } = string.Empty;
    public int BuildCatalogTileSize { get; set; } = 16;
    public List<VillagePocCatalogItem> BuildCatalog { get; set; } = new();
}

public class VillagePocMap
{
    [JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string MapKind { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public int TileSize { get; set; } = 32;
    public string BackgroundColor { get; set; } = "#9fd18b";
    public string AccentColor { get; set; } = "#5d8f4c";
    public string? BaseTileTextureUrl { get; set; }

    /// <summary>
    /// The tileset this map's sprite keys resolve against. The client loads the
    /// matching definition file and draws from the sheet; anything whose key is
    /// missing falls back to a primitive so an unfinished tileset degrades
    /// rather than rendering nothing.
    /// </summary>
    public string? TilesetKey { get; set; }
    /// <summary>
    /// True when the local member may edit the whole map. Outdoor property
    /// owners normally edit individual <see cref="VillagePocPlot.CanEdit"/>
    /// bounds instead.
    /// </summary>
    public bool CanEdit { get; set; }
    [JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]
    public long? ParentBuildingId { get; set; }
    public VillagePocPoint? SpawnTile { get; set; }
    public List<VillagePocPlot> Plots { get; set; } = new();
    public List<VillagePocDecoration> GroundTiles { get; set; } = new();
    public List<VillagePocDecoration> Decorations { get; set; } = new();
    public List<VillagePocBuilding> Buildings { get; set; } = new();
    public List<VillagePocRect> BlockedTiles { get; set; } = new();
    public List<VillagePocPortal> Portals { get; set; } = new();
}

public class VillagePocPoint
{
    public int X { get; set; }
    public int Y { get; set; }
}

public class VillagePocRect
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

public class VillagePocPortal
{
    public string Kind { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    [JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]
    public long? TargetMapId { get; set; }
    public int? TargetX { get; set; }
    public int? TargetY { get; set; }
    [JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]
    public long? BuildingId { get; set; }
    public string Color { get; set; } = "#fff2a8";
}

public class VillagePocPlot
{
    [JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    [JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]
    public long? OwnerMemberId { get; set; }
    public string? OwnerName { get; set; }
    public bool IsOwnedByLocalMember { get; set; }
    public bool CanEdit { get; set; }
    public bool ForSale { get; set; }
    public decimal Price { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string BorderColor { get; set; } = "#5d8f4c";
    public string FillColor { get; set; } = "#00000000";
}

public class VillagePocDecoration
{
    [JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]
    public long Id { get; set; }
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// Logical sprite key into the map's tileset.
    /// </summary>
    public string? DefinitionKey { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int ZIndex { get; set; }
    public string Color { get; set; } = "#ffffff";
    public string? TextureUrl { get; set; }
    public bool BlocksMovement { get; set; }
    public int Rotation { get; set; }
    [JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]
    public long? OwnerMemberId { get; set; }
    public bool IsOwnedByLocalMember { get; set; }
}

public class VillagePocCatalogItem
{
    public string Kind { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; } = 1;
    public int Height { get; set; } = 1;
    public int FootprintWidth { get; set; } = 1;
    public int FootprintHeight { get; set; } = 1;
    public bool BlocksMovement { get; set; }
}

public class VillagePocBuilding
{
    [JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string Color { get; set; } = "#c68f56";
    public string RoofColor { get; set; } = "#8f4f2a";
    public string Hint { get; set; } = string.Empty;

    /// <summary>
    /// Logical sprite key into the map's tileset.
    /// </summary>
    public string? SpriteKey { get; set; }
    [JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]
    public long? InteriorMapId { get; set; }
    [JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]
    public long? ChannelId { get; set; }
    public string? ChannelName { get; set; }
    [JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]
    public long? ChatChannelId { get; set; }
    public string? ChatChannelName { get; set; }
    public Valour.Shared.Models.ChannelTypeEnum? ChannelType { get; set; }
    public Valour.Shared.Models.VillageVoiceMode VoiceMode { get; set; }
    [JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]
    public long? OwnerMemberId { get; set; }
    public string? OwnerName { get; set; }
    public bool IsOwnedByLocalMember { get; set; }
    public bool ForSale { get; set; }
    public decimal Price { get; set; }
    public VillagePocPoint? EntranceTile { get; set; }
    public List<VillagePocRect> CollisionRects { get; set; } = new();
}

public class VillagePocCharacter
{
    [JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]
    public long UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    [JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]
    public long MapId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public bool IsLocalPlayer { get; set; }

    /// <summary>
    /// The avatar drawn for this character in the village runtime. Villages use
    /// existing member avatars rather than authored sprites, so this is the
    /// member avatar where one is set and the user avatar otherwise.
    /// </summary>
    public string AvatarUrl { get; set; } = string.Empty;

    /// <summary>
    /// Ring colour behind the avatar. Kept so characters stay distinguishable
    /// while an avatar is still loading or fails to load.
    /// </summary>
    public string AccentColor { get; set; } = "#4780d9";
}
