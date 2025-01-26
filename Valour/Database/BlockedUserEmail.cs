using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Valour.Database;

/// <summary>
/// Used to block malicious email hosts
/// </summary>
[Table("blocked_user_emails")]
public class BlockedUserEmail
{
    ///////////////////////
    // Entity Properties //
    ///////////////////////
    
    [Key]
    [Column("email")]
    public string Email { get; set; }

    public static void SetUpDbModel(ModelBuilder builder)
    {
        builder.Entity<BlockedUserEmail>(e =>
        {
            // ToTable
            e.ToTable("blocked_user_emails");
            
            // key
            e.HasKey(x => x.Email);
            
            // Properties
            e.Property(x => x.Email)
                .HasColumnName("email");
        });
    }
}
