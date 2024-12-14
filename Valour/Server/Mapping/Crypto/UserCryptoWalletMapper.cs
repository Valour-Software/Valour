using Valour.Server.Models.Crypto;

namespace Valour.Server.Mapping.Crypto;

public static class UserCryptoWalletMapper
{
    public static UserCryptoWallet ToModel(this Valour.Database.Crypto.UserCryptoWallet wallet)
    {
        if (wallet is null)
            return null;
        
        return new UserCryptoWallet()
        {
            Id = wallet.Id,
            UserId = wallet.UserId,
            WalletPublicKey = wallet.WalletPublicKey,
            LastVlrcBalance = wallet.LastVlrcBalance,
            WalletType = wallet.WalletType
        };
    }
    
    public static Valour.Database.Crypto.UserCryptoWallet ToDatabase(this UserCryptoWallet wallet)
    {
        if (wallet is null)
            return null;
        
        return new Valour.Database.Crypto.UserCryptoWallet()
        {
            Id = wallet.Id,
            UserId = wallet.UserId,
            WalletPublicKey = wallet.WalletPublicKey,
            LastVlrcBalance = wallet.LastVlrcBalance,
            WalletType = wallet.WalletType
        };
    }
}