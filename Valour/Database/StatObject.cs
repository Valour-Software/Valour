using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Valour.Database;

/*  Valour (TM) - A free and secure chat client
 *  Copyright (C) 2024 Valour Software LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */


/// <summary>
/// This represents a user within a planet and is used to represent membership
/// </summary>
[Table("stat_objects")]
public class StatObject
{
    ///////////////////////
    // Entity Properties //
    ///////////////////////
    
    /// <summary>
    /// The Id of this object
    /// </summary>
    [Key]
    [Column("id")]
    public long Id {get; set; }

    [Column("messages_sent")]
    public int MessagesSent { get; set; }

    [Column("user_count")]
    public int UserCount { get; set; }

    [Column("planet_count")]
    public int PlanetCount { get; set; }

    [Column("planet_member_count")]
    public int PlanetMemberCount { get; set; }

    [Column("channel_count")]
    public int ChannelCount { get; set; }

    [Column("category_count")]
    public int CategoryCount { get; set; }

    [Column("message_day_count")]
    public int MessageDayCount { get; set; }

    [Column("time_created")] 
    public DateTime TimeCreated { get; set; }


    public static void SetUpDbModel(ModelBuilder builder)
    {
        builder.Entity<StatObject>(e =>
        {
            // ToTable
            e.ToTable("stat_objects");
            
            // Key
            e.HasKey(x => x.Id);
            
            // Properties
            e.Property(x => x.Id)
                .HasColumnName("id");
            
            e.Property(x => x.MessagesSent)
                .HasColumnName("messages_sent");
            
            e.Property(x => x.UserCount)
                .HasColumnName("user_count");
            
            e.Property(x => x.PlanetCount)
                .HasColumnName("planet_count");
            
            e.Property(x => x.PlanetMemberCount)
                .HasColumnName("planet_member_count");
            
            e.Property(x => x.ChannelCount)
                .HasColumnName("channel_count");
            
            e.Property(x => x.CategoryCount)
                .HasColumnName("category_count");
            
            e.Property(x => x.MessageDayCount)
                .HasColumnName("message_day_count");

            e.Property(x => x.TimeCreated)
                .HasColumnName("time_created")
                .HasConversion(
                    x => x,
                    x => new DateTime(x.Ticks, DateTimeKind.Utc)
                );
            
            // Relationships
            
            // Indices
            
            e.HasIndex(x => x.Id)
                .IsUnique();
        });
    }
}

