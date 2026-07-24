namespace Valour.Shared.Villages;

public class VillagePocScene
{
    public long PlanetId { get; set; }
    public string PlanetName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public long StartingMapId { get; set; }
    public List<VillagePocMap> Maps { get; set; } = new();
    public List<VillagePocCharacter> Characters { get; set; } = new();
}

public class VillagePocMap
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string MapKind { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public int TileSize { get; set; } = 32;
    public string BackgroundColor { get; set; } = "#9fd18b";
    public string AccentColor { get; set; } = "#5d8f4c";
    public string? BaseTileTextureUrl { get; set; }
    public long? ParentBuildingId { get; set; }
    public VillagePocPoint? SpawnTile { get; set; }
    public List<VillagePocPlot> Plots { get; set; } = new();
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
    public long? TargetMapId { get; set; }
    public int? TargetX { get; set; }
    public int? TargetY { get; set; }
    public long? BuildingId { get; set; }
    public string Color { get; set; } = "#fff2a8";
}

public class VillagePocPlot
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string BorderColor { get; set; } = "#5d8f4c";
    public string FillColor { get; set; } = "#00000000";
}

public class VillagePocDecoration
{
    public string Kind { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string Color { get; set; } = "#ffffff";
    public string? TextureUrl { get; set; }
    public bool BlocksMovement { get; set; }
}

public class VillagePocBuilding
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string Color { get; set; } = "#c68f56";
    public string RoofColor { get; set; } = "#8f4f2a";
    public string Hint { get; set; } = string.Empty;
    public long? InteriorMapId { get; set; }
    public long? ChannelId { get; set; }
    public string? ChannelName { get; set; }
    public VillagePocPoint? EntranceTile { get; set; }
    public List<VillagePocRect> CollisionRects { get; set; } = new();
}

public class VillagePocCharacter
{
    public long UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public long MapId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public bool IsLocalPlayer { get; set; }
    public string BodyColor { get; set; } = "#f4d1b5";
    public string HairColor { get; set; } = "#5a3825";
    public string TopColor { get; set; } = "#4780d9";
    public string BottomColor { get; set; } = "#385068";
}
