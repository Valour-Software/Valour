using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Valour.Shared.Authorization;


namespace Valour.Server.Api.Dynamic;

public class UserWalletApi
{
    [ValourRoute(HttpVerbs.Post, "api/userWallet/nonce")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> GenerateNonce(
        IWalletService walletService,
        TokenService tokenService,
        ILogger<UserWalletApi> logger)
    {
        try
        {
            var token = await tokenService.GetCurrentTokenAsync();
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
    public static async Task<IResult> GenerateSignature(
        [FromBody] JsonElement data,
        IWalletService walletService,
        TokenService tokenService)
    {
        try
        {
            if (!data.TryGetProperty("Nonce", out var nonceProp) ||
                !data.TryGetProperty("PublicKey", out var publicKeyProp) ||
                !data.TryGetProperty("Signature", out var signatureProp)||
                !data.TryGetProperty("Vlrc", out var vlrcProp))
            {
                return Results.BadRequest(new { error = " Missing required fields." });
            }

            var vlrc = vlrcProp.GetString();
            var nonce = nonceProp.GetString();
            var publicKey = publicKeyProp.GetString();
            var signature = signatureProp.GetString();
            var token = await tokenService.GetCurrentTokenAsync();
            
            var success = await walletService.RegisterWallet(token.UserId, nonce, publicKey, signature,vlrc);
            
            return !success
              ?  Results.Ok(false) 
              : Results.Ok(true);
        }
        catch (Exception ex)
        {
            return Results.Problem("Internal server error. Please check the server logs.");
        }
    }

    
    [ValourRoute(HttpVerbs.Post, "api/userWallet/verifyWallet")]
    public static async Task<IResult> UserHasWallet([FromQuery] string publicKey,
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
        
        var success = await walletService.IsWalletRegistered(publicKey, token.UserId);
        
        return !success
            ? Results.Ok(false)
            : Results.Ok(true);
    }

    
    [ValourRoute(HttpVerbs.Post, "api/userWallet/disconnect")]
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
        return !success
            ? Results.Ok(false)
            : Results.Ok(true);
    }


    [ValourRoute(HttpVerbs.Get, "api/userWallet/isConnected")]
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
        return !success
            ? Results.Ok(false)
            : Results.Ok(true);
    }

    [ValourRoute(HttpVerbs.Get, "api/userWallet/vlrcBalance")]
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