namespace Valour.Shared.Models;

public class VoiceChannelParticipantsUpdate
{
    public long PlanetId { get; set; }
    public long ChannelId { get; set; }
    public List<long> UserIds { get; set; } = new();
}
