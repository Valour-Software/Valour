using Valour.Server.Database.Items.Channels.Planets;

namespace Valour.Server.Database.Items.Channels;

[Table("channels")]
[JsonDerivedType(typeof(PlanetChannel), typeDiscriminator: nameof(PlanetChannel))]
public class Channel : Item
{
    [Column("time_last_active")]
    public DateTime TimeLastActive { get; set; }

    [Column("state")]
    public string State { get; set; }
}
