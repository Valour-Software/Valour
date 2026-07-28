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
