using Microsoft.EntityFrameworkCore;
using Valour.Shared.Models;

namespace Valour.Database;

public class UserPrivateInfo : ISharedUserPrivateInfo
{
    ///////////////////////////
    // Relational Properties //
    ///////////////////////////
    
    public virtual User User { get; set; }
    
    ///////////////////////
    // Entity Properties //
    ///////////////////////

    /// <summary>
    /// The user's email address
    /// </summary>
    public string Email { get; set; }

    /// <summary>
    /// True if the email is verified
    /// </summary>
    public bool Verified { get; set; }

    /// <summary>
    /// The user this email belongs to
    /// </summary>
    public long UserId { get; set; }
    
    /// <summary>
    /// The date of birth of the user (for COPPA and GDPR compliance)
    /// </summary>
    public DateTime? BirthDate { get; set; }
    
    /// <summary>
    /// Locality is used for data localization and other compliance purposes
    /// </summary>
    public Locality? Locality { get; set; }
    
    /// <summary>
    /// If the user joined valour from a specific invite code, it is stored here
    /// </summary>
    public string JoinInviteCode { get; set; }
    
    /// <summary>
    /// Used to identify where a user may have joined from - YouTube, Twitter, etc.
    /// We need this because we don't use GA or any other tracking software to get
    /// some idea of where our users are coming from.
    /// </summary>
    public string JoinSource { get; set; }

    public static void SetUpDbModel(ModelBuilder builder)
    {
        builder.Entity<UserPrivateInfo>(e =>
        {
            // ToTable
            e.ToTable("user_emails");

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
                .HasColumnName("locality");

            e.Property(x => x.JoinInviteCode)
                .HasColumnName("join_invite_code");

            e.Property(x => x.JoinSource)
                .HasColumnName("join_source");

            // Relationships

            e.HasOne(x => x.User)
                .WithOne(x => x.PrivateInfo);

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

