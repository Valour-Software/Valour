using Microsoft.EntityFrameworkCore;

namespace Valour.Database.Crypto;

/// <summary>
/// A crypto wallet attached to a Valour user
/// </summary>
public class UserCryptoWallet
{
    public virtual User User { get; set; }
    
    public long Id { get; set; }
    public long UserId { get; set; } 
    public string WalletPublicKey { get; set; }
    public long LastVlrcBalance { get; set; }
    public bool AirdropReceived { get; set; }
    public string AirdropVerificationId { get; set; }
    public string WalletType { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<UserCryptoWallet>(e =>
        {
            e.ToTable("user_crypto_wallets");
            
            e.Property(x => x.Id)
                .HasColumnName("id");
            
            e.Property(x => x.UserId)
                .HasColumnName("user_id");
            
            e.Property(x => x.WalletPublicKey)
                .HasColumnName("wallet_public")
                .HasMaxLength(44);
            
            e.Property(x => x.LastVlrcBalance)
                .HasColumnName("last_vlrc_balance");

            e.Property(x => x.AirdropReceived)
                .HasColumnName("airdrop_rec");

            e.Property(x => x.AirdropVerificationId)
                .HasColumnName("airdrop_verif_id");

            e.Property(x => x.WalletType)
                .HasColumnName("wallet_type");
            
            e.HasKey(x => x.Id);
            
            e.HasIndex(x => x.AirdropVerificationId)
                .IsUnique();
            
            e.HasOne(x => x.User)
                .WithMany(x => x.CryptoWallets)
                .HasForeignKey(x => x.UserId);
        });
    }
}