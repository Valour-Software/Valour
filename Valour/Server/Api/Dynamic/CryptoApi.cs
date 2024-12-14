using System.Web.Http;
using Valour.Shared.Authorization;
using Valour.Shared.Models.Crypto;

namespace Valour.Server.Api.Dynamic;

public class CryptoApi
{
    [ValourRoute(HttpVerbs.Post, "api/crypto/me/wallets")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> AddWalletInfo(
        UserService userService,
        CryptoService cryptoService,
        [FromBody] AddWalletRequest request)
    {
        var user = await userService.GetCurrentUserAsync();

        var result = await cryptoService.AddWalletInfo(user.Id, request.WalletPubKey, request.WalletType);

        if (!result.Success)
        {
            return ValourResult.Problem(result.Message);
        }

        return ValourResult.Json(result.Data);
    }
    
    [ValourRoute(HttpVerbs.Post, "api/crypto/me/wallets/{walletId}/refresh")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> RefreshBalance(
        UserService userService,
        CryptoService cryptoService,
        long walletId)
    {
        var user = await userService.GetCurrentUserAsync();
        
        var wallet = await cryptoService.GetWallet(walletId);
        
        if (wallet is null)
            return ValourResult.NotFound("Wallet not found");

        if (wallet.UserId != user.Id)
            return ValourResult.Forbid("You do not own this wallet");

        var result = await cryptoService.RefreshWalletBalance(walletId);

        if (!result.Success)
        {
            return ValourResult.Problem(result.Message);
        }

        return ValourResult.Json(result.Data);
    }
    
    [ValourRoute(HttpVerbs.Get, "api/crypto/me/wallets")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> GetWalletInfo(
        UserService userService,
        CryptoService cryptoService)
    {
        var user = await userService.GetCurrentUserAsync();

        var result = await cryptoService.GetWallets(user.Id);

        return ValourResult.Json(result);
    }
    
    [ValourRoute(HttpVerbs.Delete, "api/crypto/me/wallets/{walletId}")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> DeleteWalletInfo(
        UserService userService,
        CryptoService cryptoService,
        long walletId)
    {
        var user = await userService.GetCurrentUserAsync();

        // Ensure user owns the wallet
        var wallet = await cryptoService.GetWallet(walletId);
        if (wallet is null)
            return ValourResult.NotFound("Wallet not found");
        
        if (wallet.UserId != user.Id)
            return ValourResult.Forbid("You do not own this wallet");
        
        var result = await cryptoService.DeleteWallet(walletId);

        if (!result.Success)
        {
            return ValourResult.Problem(result.Message);
        }

        return ValourResult.Ok();
    }
}