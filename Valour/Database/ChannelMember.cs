using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
namespace Valour.Database;

/// <summary>
/// Channel members represent members of a channel that is not a planet channel
/// In direct message channels there will only be two members, but in group channels there can be more
/// </summary>
[Table("channel_members")]
public class ChannelMember
{
    ///////////////////////////
    // Relational Properties //
    ///////////////////////////
    
    [ForeignKey("ChannelId")]
    public virtual Channel Channel { get; set; }
    
    [ForeignKey("UserId")]
    public virtual User User { get; set; }
    
    ///////////////////////
    // Entity Properties //
    ///////////////////////
    
    /// <summary>
    /// Id of the member
    /// </summary>
    [Column("id")]
    public long Id { get; set; }
    
    /// <summary>
    /// Id of the channel this member belongs to
    /// </summary>
    [Column("channel_id")]
    public long ChannelId { get; set; }
    
    /// <summary>
    /// Id of the user that has this membership
    /// </summary>
    [Column("user_id")]
    public long UserId { get; set; }


    public static void SetUpDDModel(ModelBuilder builder)
    {
        builder.Entity<ChannelMember>(e =>
        {
            // ToTable

            e.ToTable("channel_members");

            // Key

            e.HasKey(x => x.Id);

            // Properties

            e.Property(x => x.ChannelId)
                .HasColumnName("channel_id");

            e.Property(x => x.UserId)
                .HasColumnName("user_id");

            e.Property(x => x.Id)
                .HasColumnName("id");

            // Relationships

            e.HasOne(x => x.User)
                .WithMany(x => x.ChannelMembership)
                .HasForeignKey(x => x.UserId);

            e.HasOne(x => x.Channel)
                .WithMany(x => x.Members)
                .HasForeignKey(x => x.ChannelId);
            
            // Indices
            
            e.HasIndex(x => x.Id)
                .IsUnique();
            
            e.HasIndex(x => new { x.ChannelId, x.UserId });

        });
    }
}