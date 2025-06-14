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

    
    public async Task<TaskResult> RegisterWallet(long userId, string nonce, string publicKey, string signature
        ,string vlrc, string provider)
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
        
        if (signatureBytes.Length != 64 || publicKeyBytes.Length != 32) 
        { 
            _logger.LogWarning("Invalid signature or public key length.");
            return TaskResult.FromFailure("Invalid signature",400, 
                "Invalid signature or public key length.");
        }

        if (age > TimeSpan.FromMinutes(5))
        {
            _logger.LogWarning("Nonce has expired (was created {AgeMinutes} minutes ago)", age.TotalMinutes);
            return TaskResult.FromFailure("Nonce has expired",400, 
                $"Nonce has expired (was created {age.Minutes})");
        }
        

        if (alreadyRegistered)
        {
            _logger.LogWarning("This wallet is already in use");
            return  TaskResult.FromFailure("Wallet in use",409, 
                "This wallet is already in use");
        }

        if (walletExist != null)
        {
            if (!Ed25519.Verify(signatureBytes, messageBytes, publicKeyBytes))
            {
                return  TaskResult.FromFailure("Error with signature", 409,
                    "Verification with nonce is failed!");
            }

            walletExist.IsConnected = true;
            _db.UserCryptoWallets.Update(walletExist);
            await _db.SaveChangesAsync();

           
            return  TaskResult.FromSuccess("Verification signature has been successfully!");
            
        }
        
        var userWallet = new UserCryptoWallet
        { 
            Id=Guid.NewGuid().ToString(), 
            UserId = userId, 
            WalletPublic = publicKey, 
            LastVlrcBalance = vlrcLong, 
            IsConnected = true, 
            WalletType=provider
        };
        await _db.UserCryptoWallets.AddAsync(userWallet); 
        await _db.SaveChangesAsync();
        return TaskResult.FromSuccess("Wallet has been added successfully");
    }
    
    public async Task<TaskResult> DisconnectWallet(string publicKey, long userId)
    {
        var wallet = await _db.UserCryptoWallets.FirstOrDefaultAsync(x => x.WalletPublic == publicKey 
                                                                          && x.UserId == userId);
        if (wallet == null)
        {
            return  TaskResult.FromFailure("An error occurred while disconnecting the wallet.",400, 
                "No public key is associated with this user.");

        } 
        
        wallet.IsConnected = false;
        _db.UserCryptoWallets.Update(wallet);
        await _db.SaveChangesAsync();
        return TaskResult.FromSuccess("Wallet has been disconnected successfully");
    }

    public async Task<TaskResult> IsConnected(string publicKey, long userId)
    {
        if (await _db.UserCryptoWallets.AnyAsync(u => u.WalletPublic == publicKey
                                                      && u.UserId == userId && u.IsConnected))
        {
            return  TaskResult.FromSuccess("Wallet is connected successfully");
        }

        return  TaskResult.FromFailure("Wallet isn't  connected",400, 
            "The wallet isn't connected");
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
    Task<TaskResult> RegisterWallet(long userId, string nonce, string publicKey, string signature,string vlrc,string provider);
    Task<TaskResult> DisconnectWallet(string publicKey, long userId);
    Task<TaskResult> IsConnected(string publicKey, long userId);

    Task<long> VlrcBalance(string publicKey, long userId);
}

