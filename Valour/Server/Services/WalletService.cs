using System.Text;
using Chaos.NaCl;
using Valour.Database;
using Valour.Server.Email;
using Valour.Server.Users;
using Valour.Server.Utilities;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Models;
using AuthToken = Valour.Server.Models.AuthToken;
using EmailConfirmCode = Valour.Server.Models.EmailConfirmCode;
using PasswordRecovery = Valour.Server.Models.PasswordRecovery;
using Planet = Valour.Server.Models.Planet;
using TenorFavorite = Valour.Server.Models.TenorFavorite;
using User = Valour.Server.Models.User;
using UserChannelState = Valour.Server.Models.UserChannelState;
using UserPrivateInfo = Valour.Server.Models.UserPrivateInfo;
using UserProfile = Valour.Server.Models.UserProfile;

namespace Valour.Server.Services;

public class WalletService : IWalletService
{
    private readonly ValourDb _db;
    private readonly ILogger<WalletService> _logger;

    /// <summary>
    /// The stored user for the current request
    /// </summary>

    public WalletService(
        ValourDb db,
        ILogger<WalletService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<string> GenerateNonce(long userId, string publicKey)
    {
        var nonce = Guid.NewGuid();
        UserCryptoNonce entry = new UserCryptoNonce
        {
            UserId = userId,
            Nonce = nonce,
            Used = true,
            CreatedAt = DateTime.UtcNow
            
        };
        await _db.UserCryptoNonces.AddAsync(entry);
        await _db.SaveChangesAsync();
        return nonce.ToString();
    }

    
    public async Task<bool> RegisterWallet(long userId, string nonce, string publicKey, string signature)
    {
        
        var nonceEntry = await _db.UserCryptoWallets
            .Where(x => x.UserId == userId &&
                        x.WalletPublic == publicKey)
            .FirstOrDefaultAsync();
        
        var alreadyRegistered = await _db.UserCryptoWallets
            .AnyAsync(x => x.WalletPublic == publicKey && x.UserId != userId);

        if (alreadyRegistered || nonceEntry != null)
        {
            _logger.LogWarning("This wallet is already in use");
            return false;
        }
        var messageBytes = Encoding.UTF8.GetBytes(nonce); 
        var publicKeyBytes = Solnet.Wallet.Utilities.Encoders.Base58.DecodeData(publicKey); 
        var signatureBytes = Solnet.Wallet.Utilities.Encoders.Base58.DecodeData(signature);
        if (signatureBytes.Length != 64 || publicKeyBytes.Length != 32) 
        { 
            _logger.LogWarning("Invalid signature or public key length."); 
            return false;
        }
        
        if (!Ed25519.Verify(signatureBytes, messageBytes, publicKeyBytes)) 
        { 
            _logger.LogWarning("The signature does not match the provided nonce."); 
            return false;
        }

        var userWallet = new UserCryptoWallet
        { 
            Id=signature, 
            UserId = userId, 
            WalletPublic = publicKey, 
            LastVlrcBalance = 0, 
            IsConnected = true, 
            WalletType="Solflare"
        };
        await _db.UserCryptoWallets.AddAsync(userWallet); 
        await _db.SaveChangesAsync();
        return true;
    }
    
    public async Task<bool> IsWalletRegistered(string publicKey, long userId)
    {
        var wallet = await _db.UserCryptoWallets
            .FirstOrDefaultAsync(u => u.WalletPublic == publicKey && u.UserId == userId);

        if (wallet == null) return false;
        if (wallet.IsConnected) return true;
        wallet.IsConnected = true;
        await _db.SaveChangesAsync();
        return true;

    }


    public async Task<bool> DisconnectWallet(string publicKey, long userId)
    {
        var wallet = await _db.UserCryptoWallets.FirstOrDefaultAsync(x => x.WalletPublic == publicKey && x.UserId != userId);
        if (wallet == null) return false;
        
        wallet.IsConnected = false;
        await _db.SaveChangesAsync();
        return true;

    }
}


public interface IWalletService
{
    Task<string> GenerateNonce(long userId, string publicKey);
    Task<bool> RegisterWallet(long userId, string nonce, string publicKey, string signature);
    Task<bool> IsWalletRegistered(string publicKey, long userId); 
    Task<bool> DisconnectWallet(string publicKey, long userId);
}

