
using Microsoft.EntityFrameworkCore;

namespace Valour.Database;

public class UserCryptoNonce
{
    
    ///////////////////////
    // Entity Properties //
    ///////////////////////

    /// <summary>
    /// The nonce for the wallet connect
    /// </summary>
    public Guid Nonce { get; set; }
    

    /// <summary>
    /// The user this code is verifying
    /// </summary>
    public long UserId { get; set; }
    

    /// <summary>
    /// The created time
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Used or not
    /// </summary>
    public bool Used { get; set; }


    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<UserCryptoNonce>(e =>
        {
            // Table
            e.ToTable("user_crypto_nonces");
            
            // Keys
            e.HasKey(x => x.Nonce);

            // Properties
            e.Property(x => x.Nonce)
                .HasColumnName("nonce");
            
            e.Property(x=>x.UserId)
                .HasColumnName("user_id");
            
            e.Property(x=>x.CreatedAt)
                .HasColumnName("created_at");
            
            e.Property(x=>x.Used)
                .HasColumnName("used");
        });
    }
}

