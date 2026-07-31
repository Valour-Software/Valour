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
/// One atomic in-world edit. Paint replaces the ground tile at a coordinate,
/// furnish places a depth-sorted object, and erase removes the supplied object.
/// The server derives collision and ownership rather than trusting the client.
/// </summary>
public class VillageBuildRequest
{
    public VillageBuildAction Action { get; set; }
    public string? DefinitionKey { get; set; }
    public int X { get; set; }
    public int Y { get; set; }

    [System.Text.Json.Serialization.JsonNumberHandling(
        System.Text.Json.Serialization.JsonNumberHandling.WriteAsString |
        System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString)]
    public long? ObjectId { get; set; }
}

public class VillageBuildResult
{
    public VillagePocDecoration? Decoration { get; set; }

    [System.Text.Json.Serialization.JsonNumberHandling(
        System.Text.Json.Serialization.JsonNumberHandling.WriteAsString |
        System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString)]
    public List<long> RemovedObjectIds { get; set; } = new();
}
