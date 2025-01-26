using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Valour.Database;

/// <summary>
/// Used for password recovery
/// </summary>
[Table("password_recoveries")]
public class PasswordRecovery
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
    [Column("code")]
    public string Code { get; set; }

    [Column("user_id")]
    public long UserId { get; set; }


    public static void SetUpDbModel(ModelBuilder builder)
    {
        builder.Entity<PasswordRecovery>(e =>
        {
            // ToTable   
            e.ToTable("password_recovery");
            
            // Key
            e.HasKey(x => x.Code);
            
            // Properties
            e.Property(x => x.Code)
                .HasColumnName("code");
            
            e.Property(x => x.UserId)
                .HasColumnName("user_id");
            
            // Relationships
            
            // Indices
            
            e.HasIndex(x => x.UserId);

        });
    }
}

