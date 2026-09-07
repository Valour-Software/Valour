namespace Valour.Shared.Villages;

/// <summary>
/// A short-lived call channel and its associated chat, created for a village
/// area that is not bound to a permanent planet channel.
/// </summary>
public class VillageEphemeralRoom
{
    public long PlanetId { get; set; }
    public long MapId { get; set; }
    public long? BuildingId { get; set; }
    public long ChannelId { get; set; }
    public long ChatChannelId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool SupportsVideo { get; set; } = true;
}
