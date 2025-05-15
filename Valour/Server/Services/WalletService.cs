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

    public async Task<string> GenerateNonce(long userId)
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

    
    public async Task<bool> RegisterWallet(long userId, string nonce, string publicKey, string signature,string vlrc)
    {
        
        var alreadyRegistered = await _db.UserCryptoWallets.AnyAsync(x => x.WalletPublic == publicKey 
                                                                          && x.UserId != userId);

        var nonceEntry = await _db.UserCryptoNonces.FirstOrDefaultAsync(n => n.Nonce.ToString() == nonce);
        
        var walletExist = await _db.UserCryptoWallets.FirstOrDefaultAsync(u=>u.UserId == userId 
                                                                        && u.WalletPublic == publicKey);
        
        
        var vlrcLong = long.Parse(vlrc);
        var messageBytes = Encoding.UTF8.GetBytes(nonce); 
        var publicKeyBytes = Solnet.Wallet.Utilities.Encoders.Base58.DecodeData(publicKey); 
        var signatureBytes = Solnet.Wallet.Utilities.Encoders.Base58.DecodeData(signature);
        var age = DateTime.UtcNow - nonceEntry.CreatedAt;
        
        if (age > TimeSpan.FromMinutes(5))
        {
            _logger.LogWarning("Nonce has expired (was created {AgeMinutes} minutes ago)", age.TotalMinutes);
            return false;
        }
        

        if (alreadyRegistered)
        {
            _logger.LogWarning("This wallet is already in use");
            return false;
        }

        if (walletExist != null)
        {
            walletExist.IsConnected = true;
            _db.UserCryptoWallets.Update(walletExist);
            await _db.SaveChangesAsync();
            
            return Ed25519.Verify(signatureBytes, messageBytes, publicKeyBytes);
        }

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
            Id=Guid.NewGuid().ToString(), 
            UserId = userId, 
            WalletPublic = publicKey, 
            LastVlrcBalance = vlrcLong, 
            IsConnected = true, 
            WalletType="Solflare"
        };
        await _db.UserCryptoWallets.AddAsync(userWallet); 
        await _db.SaveChangesAsync();
        return true;
    }
    
    public async Task<bool> IsWalletRegistered(string publicKey,long userId)
    {
        var wallet = await _db.UserCryptoWallets.FirstOrDefaultAsync(x => x.WalletPublic == publicKey
                                                                          && x.UserId == userId);
        if (wallet == null) return false;
        wallet.IsConnected = true;
        _db.UserCryptoWallets.Update(wallet);
        await _db.SaveChangesAsync();
        return true;
    }


    public async Task<bool> DisconnectWallet(string publicKey, long userId)
    {
        var wallet = await _db.UserCryptoWallets.FirstOrDefaultAsync(x => x.WalletPublic == publicKey 
                                                                          && x.UserId == userId);
        if (wallet == null) return false;
         wallet.IsConnected = false;
        _db.UserCryptoWallets.Update(wallet);
        await _db.SaveChangesAsync();
        return true;

    }

    public async Task<bool> IsConnected(string publicKey, long userId)
    {
        return await _db.UserCryptoWallets.AnyAsync(u=>u.WalletPublic == publicKey 
                                                       && u.UserId == userId && u.IsConnected);
    }

    public async Task<long> VlrcBalance(string publicKey, long userId)
    {
        var wallet = await _db.UserCryptoWallets.FirstOrDefaultAsync(x => x.WalletPublic == publicKey 
                                                                          && x.UserId == userId);
        return wallet.LastVlrcBalance;
    }
}


public interface IWalletService
{
    Task<string> GenerateNonce(long userId);
    Task<bool> RegisterWallet(long userId, string nonce, string publicKey, string signature,string vlrc);
    Task<bool> IsWalletRegistered(string publicKey, long userId); 
    Task<bool> DisconnectWallet(string publicKey, long userId);
    Task<bool> IsConnected(string publicKey, long userId);

    Task<long> VlrcBalance(string publicKey, long userId);
}

