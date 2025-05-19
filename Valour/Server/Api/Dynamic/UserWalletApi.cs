using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Valour.Shared;
using Valour.Shared.Authorization;


namespace Valour.Server.Api.Dynamic;

public class UserWalletApi
{
    
    [ValourRoute(HttpVerbs.Post, "api/userWallet/nonce")]
    [UserRequired(UserPermissionsEnum.View)]
    public static async Task<IResult> GenerateNonce(
        IWalletService walletService,
        TokenService tokenService,
        ILogger<UserWalletApi> logger)
    {
        try
        {
            var token = await tokenService.GetCurrentTokenAsync();

            if (token == null)
            {
                logger.LogInformation("Token is null. User is probably not authenticated.");
                return Results.Unauthorized();
            }
            
            var nonce = await walletService.GenerateNonce(token.UserId);
            logger.LogInformation("Generated nonce for user {UserId}", token.UserId);

            return Results.Ok(nonce);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception in GenerateNonce endpoint");
            return Results.Problem("Problem");
        }
    }
    
    [ValourRoute(HttpVerbs.Post, "api/userWallet/signature")]
    [UserRequired(UserPermissionsEnum.View)]
    public static async Task<IResult> GenerateSignature(
        [FromBody] JsonElement data,
        IWalletService walletService,
        TokenService tokenService)
    {
            if (!data.TryGetProperty("Nonce", out var nonceProp) ||
                !data.TryGetProperty("PublicKey", out var publicKeyProp) ||
                !data.TryGetProperty("Signature", out var signatureProp)||
                !data.TryGetProperty("Vlrc", out var vlrcProp)|| 
                !data.TryGetProperty("Provider", out var providerProp))
            {
                return Results.BadRequest(new { error = " Missing required fields." });
            }

            var vlrc = vlrcProp.GetString();
            var nonce = nonceProp.GetString();
            var publicKey = publicKeyProp.GetString();
            var signature = signatureProp.GetString();
            var token = await tokenService.GetCurrentTokenAsync();
            var provider = providerProp.GetString();
            
            var success = await walletService.RegisterWallet(token.UserId, nonce, publicKey, signature,vlrc,provider);
            return Results.Ok(success);

    }
    
    [ValourRoute(HttpVerbs.Post, "api/userWallet/disconnect")]
    [UserRequired(UserPermissionsEnum.View)]
    public static async Task<IResult> DisconnectWallet([FromQuery] string publicKey,
        IWalletService walletService,TokenService tokenService,
        ILogger<UserWalletApi> logger)
    {
        var token = await tokenService.GetCurrentTokenAsync();
        if (token == null)
        {
            logger.LogInformation("Token is null. User is probably not authenticated.");
            return Results.Unauthorized();
        }
        
        if (string.IsNullOrEmpty(publicKey))
        {
            logger.LogInformation("PublicKey was null or empty.");
            return Results.BadRequest(new { error = "Missing publicKey" });
        }
        
        var success = await walletService.DisconnectWallet(publicKey,token.UserId);
        return Results.Ok(success);

    }


    [ValourRoute(HttpVerbs.Get, "api/userWallet/isConnected")]
    [UserRequired(UserPermissionsEnum.View)]
    public static async Task<IResult> IsWalletConnected([FromQuery] string publicKey,
        IWalletService walletService, TokenService tokenService,
        ILogger<UserWalletApi> logger)
    {
        var token = await tokenService.GetCurrentTokenAsync();
        if (token == null)
        {
            logger.LogInformation("Token is null. User is probably not authenticated.");
            return Results.Unauthorized();
        }
        
        if (string.IsNullOrEmpty(publicKey))
        {
            logger.LogInformation("PublicKey was null or empty.");
            return Results.BadRequest(new { error = "Missing publicKey" });
        }
        var success = await walletService.IsConnected(publicKey,token.UserId);
        return Results.Ok(success);

    }

    [ValourRoute(HttpVerbs.Get, "api/userWallet/vlrcBalance")]
    [UserRequired(UserPermissionsEnum.View)]
    public static async Task<IResult> VlrcBalance([FromQuery] string publicKey,
        IWalletService walletService, TokenService tokenService,
        ILogger<UserWalletApi> logger)
    {
        var token = await tokenService.GetCurrentTokenAsync();
        if (token == null)
        {
            logger.LogInformation("Token is null. User is probably not authenticated.");
            return Results.Unauthorized();
        }
        
        if (string.IsNullOrEmpty(publicKey))
        {
            logger.LogInformation("PublicKey was null or empty.");
            return Results.BadRequest(new { error = "Missing publicKey" });
        }
        var vlrc = await walletService.VlrcBalance(publicKey,token.UserId);
        
        return Results.Ok(vlrc);
    }
    

}