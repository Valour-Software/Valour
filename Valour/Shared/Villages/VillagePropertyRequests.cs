namespace Valour.Shared.Villages;

/// <summary>
/// Edits a building's identity and channel binding. Null fields are left
/// unchanged; the channel is only touched when <see cref="UpdateChannel"/> is
/// set, because "clear the linked channel" and "leave it alone" both arrive as
/// a null <see cref="ChannelId"/> otherwise.
/// </summary>
public class VillageBuildingUpdateRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public bool UpdateChannel { get; set; }
    public long? ChannelId { get; set; }
}

public class VillagePlotUpdateRequest
{
    public string? Name { get; set; }
}

public class VillageSaleListingRequest
{
    public bool ForSale { get; set; }
    public decimal Price { get; set; }
}

public enum VillageBuildAction
{
    Paint = 0,
    Furnish = 1,
    Erase = 2,
}

/// <summary>
/// One atomic in-world edit. Paint applies a logical terrain brush and lets the
/// server resolve its transition art, furnish places a depth-sorted object, and
/// erase removes the supplied object. The server derives definitions, collision,
/// and ownership rather than trusting the client.
/// </summary>
public class VillageBuildRequest
{
    public VillageBuildAction Action { get; set; }
    public string? DefinitionKey { get; set; }
    public string? TerrainKey { get; set; }
    public string? BrushKey { get; set; }
    public List<VillageBuildCell> Cells { get; set; } = new();
    public int X { get; set; }
    public int Y { get; set; }

    [System.Text.Json.Serialization.JsonNumberHandling(
        System.Text.Json.Serialization.JsonNumberHandling.WriteAsString |
        System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString)]
    public long? ObjectId { get; set; }
}

public class VillageBuildCell
{
    public int X { get; set; }
    public int Y { get; set; }
}

public class VillageBuildResult
{
    /// <summary>
    /// True when the edit changed the map graph (for example by creating or
    /// archiving a building interior) and the client must replace its scene.
    /// </summary>
    public bool SceneChanged { get; set; }

    [System.Text.Json.Serialization.JsonNumberHandling(
        System.Text.Json.Serialization.JsonNumberHandling.WriteAsString |
        System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString)]
    public long? BuildingId { get; set; }

    [System.Text.Json.Serialization.JsonNumberHandling(
        System.Text.Json.Serialization.JsonNumberHandling.WriteAsString |
        System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString)]
    public long? InteriorMapId { get; set; }

    /// <summary>
    /// The primary changed object, retained for older clients and for
    /// single-object furnish operations.
    /// </summary>
    public VillagePocDecoration? Decoration { get; set; }

    /// <summary>
    /// Every object created or updated by the edit. Terrain paint can change
    /// the resolved art of all eight neighboring cells in the same transaction.
    /// </summary>
    public List<VillagePocDecoration> Decorations { get; set; } = new();

    [System.Text.Json.Serialization.JsonNumberHandling(
        System.Text.Json.Serialization.JsonNumberHandling.WriteAsString |
        System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString)]
    public List<long> RemovedObjectIds { get; set; } = new();
}
