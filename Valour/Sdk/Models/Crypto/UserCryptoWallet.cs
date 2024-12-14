namespace Valour.Sdk.Models.Crypto;

public class UserCryptoWallet
{
    public long Id { get; set; }
    public long UserId { get; set; } 
    public string WalletPublicKey { get; set; }
    public long LastVlrcBalance { get; set; }
    public string WalletType { get; set; }
}