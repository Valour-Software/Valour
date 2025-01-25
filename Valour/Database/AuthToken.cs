using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Valour.Shared.Models;

namespace Valour.Database;

[Table("auth_tokens")]
public class AuthToken : ISharedAuthToken
{
    ///////////////////////////
    // Relational Properties //
    ///////////////////////////

    [ForeignKey("UserId")]
    public virtual User User { get; set; }
    
    ///////////////////////
    // Entity Properties //
    ///////////////////////
    
    [Key]
    [Column("id")]
    public string Id { get; set; }

    /// <summary>
    /// The ID of the app that has been issued this token
    /// </summary>
    [Column("app_id")]
    public string AppId { get; set; }

    /// <summary>
    /// The user that this token is valid for
    /// </summary>
    [Column("user_id")]
    public long UserId { get; set; }

    /// <summary>
    /// The scope of the permissions this token is valid for
    /// </summary>
    [Column("scope")]
    public long Scope { get; set; }

    /// <summary>
    /// The time that this token was issued
    /// </summary>
    [Column("time_created")]
    public DateTime TimeCreated { get; set; }

    /// <summary>
    /// The time that this token will expire
    /// </summary>
    [Column("time_expires")]
    public DateTime TimeExpires { get; set; }

    /// <summary>
    /// The IP address this token was issued to originally
    /// </summary>
    [Column("issued_address")]
    public string IssuedAddress { get; set; }


    public static void SetUpDDModel(ModelBuilder builder)
    {
        builder.Entity<AuthToken>(e =>
        {
            // ToTable

            e.ToTable("auth_tokens");
            
            // Key
            
            e.HasKey(x => x.Id);
            
            // Properties

            e.Property(x => x.Id)
                .HasColumnName("id");
            
            e.Property(x => x.AppId)
                .HasColumnName("app_id");
            
            e.Property(x => x.UserId)
                .HasColumnName("user_id");
            
            e.Property(x => x.Scope)
                .HasColumnName("scope");

            e.Property(x => x.TimeCreated)
                .HasColumnName("time_created")
                .HasConversion(
                    x => x,
                    x => new DateTime(x.Ticks, DateTimeKind.Utc)
                );

            e.Property(x => x.TimeExpires)
                .HasColumnName("time_expires")
                .HasConversion(
                    x => x,
                    x => new DateTime(x.Ticks, DateTimeKind.Utc)
                );
            
            e.Property(x => x.IssuedAddress)
                .HasColumnName("issued_address");
            
            // Relationships

            e.HasOne(x => x.User)
                .WithMany(x => x.OwnedTokens)
                .HasForeignKey(x => x.UserId);
            
            // Indices
            
            e.HasIndex(x => x.Id)
                .IsUnique();

            e.HasIndex(x => x.UserId)
                .IsUnique();

            e.HasIndex(x => x.Scope);
        });
    }

}

