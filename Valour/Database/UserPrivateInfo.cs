using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Valour.Shared.Models;

namespace Valour.Database;

[Table("user_emails")]
public class UserPrivateInfo : ISharedUserPrivateInfo
{
    ///////////////////////////
    // Relational Properties //
    ///////////////////////////
    
    [ForeignKey("UserId")]
    [JsonIgnore]
    public virtual User User { get; set; }
    
    ///////////////////////
    // Entity Properties //
    ///////////////////////

    /// <summary>
    /// The user's email address
    /// </summary>
    [Key]
    [Column("email")]
    public string Email { get; set; }

    /// <summary>
    /// True if the email is verified
    /// </summary>
    [Column("verified")]
    public bool Verified { get; set; }

    /// <summary>
    /// The user this email belongs to
    /// </summary>
    [Column("user_id")]
    public long UserId { get; set; }
    
    /// <summary>
    /// The date of birth of the user (for COPPA and GDPR compliance)
    /// </summary>
    [Column("birth_date")]
    public DateTime? BirthDate { get; set; }
    
    /// <summary>
    /// Locality is used for data localization and other compliance purposes
    /// </summary>
    [Column("locality")]
    public Locality? Locality { get; set; }
    
    /// <summary>
    /// If the user joined valour from a specific invite code, it is stored here
    /// </summary>
    [Column("join_invite_code")]
    public string JoinInviteCode { get; set; }
    
    /// <summary>
    /// Used to identify where a user may have joined from - YouTube, Twitter, etc.
    /// We need this because we don't use GA or any other tracking software to get
    /// some idea of where our users are coming from.
    /// </summary>
    [Column("join_source")]
    public string JoinSource { get; set; }


    public static void SetUpDDModel(ModelBuilder builder)
    {
        builder.Entity<UserPrivateInfo>(e =>
        {
            // ToTable
            e.ToTable("user_private_info");
            
            // key
            e.HasKey(x => x.Email);
            
            // Property
            e.Property(x => x.Email)
                .HasColumnName("email");
            
            e.Property(x => x.Verified)
                .HasColumnName("verified");
            
            e.Property(x => x.UserId)
                .HasColumnName("user_id");

            e.Property(x => x.BirthDate)
                .HasColumnName("birth_date")
                .HasConversion(
                    x => x,
                    x => x == null ? null : new DateTime(x.Value.Ticks, DateTimeKind.Utc)
                );

            e.Property(x => x.Locality)
                .HasColumnName("locality")
                .HasConversion(
                    x => x.ToString(),
                    x => (Locality)Enum.Parse(typeof(Locality), x)
                );
            
            e.Property(x => x.JoinInviteCode)
                .HasColumnName("join_invite_code");
            
            e.Property(x => x.JoinSource)
                .HasColumnName("join_source");
            
            // Relationships
            
            e.HasOne(x => x.User)
                .WithMany(x => x.UserPrivateInfo)
                .HasForeignKey(x => x.UserId);
            
            // Indices
            
            e.HasIndex(x => x.UserId)
                .IsUnique();
            
            e.HasIndex(x => x.BirthDate);
            
            e.HasIndex(x => x.Locality);

            e.HasIndex(x => x.JoinInviteCode);
            
            e.HasIndex(x => x.Email);
        });
    }
}

