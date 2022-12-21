using Valour.Api.Items.Channels.Users;
using Valour.Server.Database.Items.Channels.Planets;

namespace Valour.Server.Database.Items.Channels;

[Table("channels")]
[JsonDerivedType(typeof(PlanetChannel), typeDiscriminator: nameof(PlanetChannel))]
[JsonDerivedType(typeof(DirectChatChannel), typeDiscriminator: nameof(DirectChatChannel))]
[JsonDerivedType(typeof(PlanetChatChannel), typeDiscriminator: nameof(PlanetChatChannel))]
[JsonDerivedType(typeof(PlanetCategoryChannel), typeDiscriminator: nameof(PlanetCategoryChannel))]
[JsonDerivedType(typeof(PlanetVoiceChannel), typeDiscriminator: nameof(PlanetVoiceChannel))]
public class Channel : Item
{
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
