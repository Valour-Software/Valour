using Microsoft.EntityFrameworkCore;

namespace Valour.Database;


public class UserCryptoWallet
{
    
    ///////////////////////
    // Entity Properties //
    ///////////////////////

    /// <summary>
    /// The unique ID of the user.
    /// </summary>
    public string Id { get; set; }
    
    /// <summary>
    /// Public address of the user's crypto wallet
    /// </summary>
    public string WalletPublic { get; set; }
    
    /// <summary>
    /// Last known balance of VLRC tokens in the wallet.
    /// </summary>
     public long LastVlrcBalance { get; set; }
    
    /// <summary>
    /// Indicates whether the user has received the airdrop.
    /// </summary>
    public bool AirdropRec { get; set; } = false;

    /// <summary>
    /// Verification ID used to track or validate airdrop claim.
    /// </summary>
     public string AirdropVerifId { get; set; }
    
    /// <summary>
    /// Associated internal user ID in the system.
    /// </summary>
     public long UserId { get; set; }
     
    /// <summary>
    /// Type of the wallet (e.g., "solana", "ethereum").
    /// </summary>
    public string WalletType { get; set; }

    /// <summary>
    /// Indicates whether the wallet is currently connected.
    /// </summary>
    public bool IsConnected { get; set; } = true;

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<UserCryptoWallet>(e =>
        {
            // Table
            e.ToTable("user_crypto_wallets");
            
            // Keys
            e.HasKey(x => x.Id);

            // Properties
            e.Property(x => x.Id)
                .HasColumnName("id");
            e.Property(x => x.WalletPublic)
                .HasColumnName("wallet_public")
                .HasMaxLength(44);
            e.Property(x => x.LastVlrcBalance)
                .HasColumnName("last_vlrc_balance");
            
            e.Property(x => x.AirdropRec)
                .HasColumnName("airdrop_rec");
            
            e.Property(x => x.AirdropVerifId)
                .HasColumnName("airdrop_verif_id");
            
            e.Property(x => x.UserId)
                .HasColumnName("user_id");
            
            e.Property(x => x.WalletType)
                .HasColumnName("wallet_type")
                .HasMaxLength(16);
            
            e.Property(x => x.IsConnected)
                .HasColumnName("is_connected");
        });

    }
}