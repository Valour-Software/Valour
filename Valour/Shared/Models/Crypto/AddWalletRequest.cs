namespace Valour.Shared.Models.Crypto;

public class AddWalletRequest
{
    public string WalletPubKey { get; set; }
    public string WalletType { get; set; }
}