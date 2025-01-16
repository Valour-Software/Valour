using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Valour.Shared.Models;


namespace Valour.Database;

[Table("reports")]
public class Report : ISharedReport
{
    /// <summary>
    /// Guid Id of the report
    /// </summary>
    
    [Column("id")]
    public string Id { get; set; }
    
    /// <summary>
    /// The time the report was created
    /// </summary>
    [Column("time_created")]
    public DateTime TimeCreated { get; set; }
    
    /// <summary>
    /// The user who sent the report
    /// </summary>
    [Column("reporting_user_id")]
    public long ReportingUserId { get; set; }
    
    /// <summary>
    /// The message id (if any) the report applies to
    /// </summary>
    [Column("message_id")]
    public long? MessageId { get; set; }
    
    /// <summary>
    /// The channel id (if any) the report applies to
    /// </summary>
    [Column("channel_id")]
    public long? ChannelId { get; set; }
    
    /// <summary>
    /// The planet id (if any) the report applies to
    /// </summary>
    [Column("planet_id")]
    public long? PlanetId { get; set; }
    
    /// <summary>
    /// The category-code of the reason of the report
    /// </summary>
    [Column("reason_code")]
    public ReportReasonCode ReasonCode { get; set; }
    
    /// <summary>
    /// The user-written reason for the report
    /// </summary>
    [Column("long_reason")]
    public string LongReason { get; set; }
    
    /// <summary>
    /// If the report has been reviewed by a moderator
    /// </summary>
    [Column("reviewed")]
    public bool Reviewed { get; set; }
    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<Report>(e =>
        {
            // Table
            e.ToTable("reports");
            
            // Keys
            e.HasKey(x => x.Id);
            
            // Properties
            e.Property(x => x.Id)
                .HasColumnName("id");
            
            e.Property(x => x.TimeCreated)
                .HasColumnName("time_created")
                .HasConversion(
                x => x,
                x => new DateTime(x.Ticks, DateTimeKind.Utc)
                );
            
            e.Property(x => x.ReportingUserId)
                .HasColumnName("reporting_user_id");
            
            e.Property(x => x.MessageId)
                .HasColumnName("message_id");
            
            e.Property(x => x.PlanetId)
                .HasColumnName("planet_id");
            
            e.Property(x => x.ChannelId)
                .HasColumnName("channel_id");
            
            e.Property(x => x.ReasonCode)
                .HasColumnName("reason_code");
            
            e.Property(x => x.LongReason)
                .HasColumnName("long_reason");
            
            e.Property(x => x.Reviewed)
                .HasColumnName("reviewed");
            
            // Relantioships
            
            e.HasOne(x => x.)
            


        });

    }
}