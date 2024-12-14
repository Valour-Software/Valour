using Solnet.Rpc;
using Solnet.Wallet;
using Valour.Server.Database;
using Valour.Server.Mapping.Crypto;
using Valour.Server.Models.Crypto;
using Valour.Shared;

namespace Valour.Server.Services;

public class CryptoService
{
    public const string VlrcAddress = "Ec6t4jq6vK2QzUytRTCF1Bi5GVbaKeZ4nryxRbhjf2b9";
    
    private readonly ValourDB _db;

    public CryptoService(ValourDB db)
    {
        _db = db;
    }
    
    private async Task<long?> GetVlrcBalance(string walletPubKey)
    {
        var solRpc = ClientFactory.GetClient(Cluster.MainNet);
        var tokenAccountsResult = await solRpc.GetTokenAccountsByOwnerAsync(walletPubKey, VlrcAddress);
        if (!tokenAccountsResult.WasSuccessful)
        {
            // Try again up to 3 times
            for (int i = 0; i < 3; i++)
            {
                await Task.Delay(500);
                
                tokenAccountsResult = await solRpc.GetTokenAccountsByOwnerAsync(walletPubKey, VlrcAddress);
                if (tokenAccountsResult.WasSuccessful)
                    break;
            }
        }
        
        // Count up lamports
        long? lamports = null;
        if (tokenAccountsResult.WasSuccessful)
        {
            lamports = 0;
            
            foreach (var tokenAccount in tokenAccountsResult.Result.Value)
            {
                lamports += (long)tokenAccount.Account.Data.Parsed.Info.TokenAmount.AmountUlong;
            }
        }

        return lamports;
    } 
    
    public async Task<TaskResult<long>> RefreshWalletBalance(long walletId)
    {
        var wallet = await _db.UserCryptoWallets.FindAsync(walletId);
        
        if (wallet is null)
            return TaskResult<long>.FromError("Wallet not found");
        
        var balance = await GetVlrcBalance(wallet.WalletPublicKey);

        if (!balance.HasValue)
        {
            return TaskResult<long>.FromError("Failed to refresh balance. Try again?");
        }
        
        wallet.LastVlrcBalance = balance.Value;
        
        await _db.SaveChangesAsync();

        return TaskResult<long>.FromData(balance.Value);
    }

    public async Task<TaskResult<UserCryptoWallet>> AddWalletInfo(long userId, string walletPubKey, string walletType)
    {
        var solRpc = ClientFactory.GetClient(Cluster.MainNet);
        
        var balance = await GetVlrcBalance(walletPubKey);

        if (!balance.HasValue)
        {
            return TaskResult<UserCryptoWallet>.FromError("Failed to get wallet. Try again?");
        }
        
        var wallet = new Valour.Database.Crypto.UserCryptoWallet()
        {
            Id = IdManager.Generate(),
            UserId = userId,
            AirdropReceived = false,
            AirdropVerificationId = null,
            LastVlrcBalance = balance.Value,
            WalletType = walletType,
            WalletPublicKey = walletPubKey
        };
        
        await _db.UserCryptoWallets.AddAsync(wallet);
        
        await _db.SaveChangesAsync();

        return TaskResult<UserCryptoWallet>.FromData(wallet.ToModel());
    }

    public async Task<List<UserCryptoWallet>> GetWallets(long userId)
    {
        var wallets = await _db.UserCryptoWallets
            .Where(x => x.UserId == userId)
            .Select(x => x.ToModel())
            .ToListAsync();
        
        return wallets;
    }
    
    public async Task<UserCryptoWallet> GetWallet(long walletId)
    {
        var wallet = await _db.UserCryptoWallets.FindAsync(walletId);
        return wallet.ToModel();
    }
    
    public async Task<TaskResult> DeleteWallet(long walletId)
    {
        var wallet = await _db.UserCryptoWallets.FindAsync(walletId);
        
        if (wallet is null)
            return TaskResult.FromError("Wallet not found");
        
        _db.UserCryptoWallets.Remove(wallet);
        
        await _db.SaveChangesAsync();

        return TaskResult.SuccessResult;
    }
}