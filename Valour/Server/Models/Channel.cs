namespace Valour.Server.Models;

[JsonDerivedType(typeof(PlanetChannel), typeDiscriminator: nameof(PlanetChannel))]
[JsonDerivedType(typeof(DirectChatChannel), typeDiscriminator: nameof(DirectChatChannel))]
[JsonDerivedType(typeof(PlanetChatChannel), typeDiscriminator: nameof(PlanetChatChannel))]
[JsonDerivedType(typeof(PlanetCategory), typeDiscriminator: nameof(PlanetCategory))]
[JsonDerivedType(typeof(PlanetVoiceChannel), typeDiscriminator: nameof(PlanetVoiceChannel))]
public class Channel : Item
{
    /// <summary>
    /// The last time this channel was active
    /// </summary>
    public DateTime TimeLastActive { get; set; }
    
    /// <summary>
    /// This is being deprecated
    /// </summary>
    public string State { get; set; }
    
    /// <summary>
    /// Soft-delete flag
    /// </summary>
    public bool IsDeleted { get; set; }
}
