using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Valour.Database;

[Table("channels_old")]
[JsonDerivedType(typeof(PlanetChannel), typeDiscriminator: nameof(PlanetChannel))]
[JsonDerivedType(typeof(DirectChatChannel), typeDiscriminator: nameof(DirectChatChannel))]
[JsonDerivedType(typeof(PlanetChatChannel), typeDiscriminator: nameof(PlanetChatChannel))]
[JsonDerivedType(typeof(PlanetCategory), typeDiscriminator: nameof(PlanetCategory))]
[JsonDerivedType(typeof(PlanetVoiceChannel), typeDiscriminator: nameof(PlanetVoiceChannel))]
public class Channel : Item
{
    ///////////////////////
    // Entity Properties //
    ///////////////////////
    
    [Column("time_last_active")]
    public DateTime TimeLastActive { get; set; }

    [Column("state")]
    public string State { get; set; }
    
    /// <summary>
    /// Soft-delete flag
    /// </summary>
    [Column("is_deleted")]
    public bool IsDeleted { get; set; }
}
