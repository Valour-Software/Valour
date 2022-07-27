namespace Valour.Server.Database.Items.Channels;

[Table("channels")]
public class Channel : Item
{
    [Column("time_last_active")]
    public DateTime TimeLastActive { get; set; }
}
