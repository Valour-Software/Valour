using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Valour.Database;

/// <summary>
/// Allows tracking of email verification codes
/// </summary>
[Table("email_confirm_codes")]
public class EmailConfirmCode
{
    ///////////////////////////
    // Relational Properties //
    ///////////////////////////
    
    [ForeignKey("UserId")]
    public virtual User User { get; set; }
    
    ///////////////////////
    // Entity Properties //
    ///////////////////////

    /// <summary>
    /// The code for the email verification
    /// </summary>
    [Key]
    [Column("code")]
    public string Code { get; set; }

    /// <summary>
    /// The user this code is verifying
    /// </summary>
    [Column("user_id")]
    public long UserId { get; set; }

    public static void SetUpDbModel(ModelBuilder builder)
    {
        builder.Entity<EmailConfirmCode>(e =>
        {
            // ToTable
            e.ToTable("email_confirm_codes");
            
            // Key
            e.HasKey(x => x.Code);
            
            // Properties
            e.Property(x => x.Code)
                .HasColumnName("code");
            
            e.Property(x => x.UserId)
                .HasColumnName("user_id");
            
            // Relationships
            e.HasOne(x => x.User)
                .WithMany(x => x.EmailConfirmCodes)
                .HasForeignKey(x => x.UserId);
            
            
            // Indices
            e.HasIndex(x => x.UserId)
                .IsUnique();
        });
    }
}

