using System.Text.Json;
using Microsoft.AspNetCore.Mvc;


namespace Valour.Server.Api.Dynamic;

public class UserWalletApi
{
    [ValourRoute(HttpVerbs.Get, "api/userWallet/nonce")]
    public static async Task<IResult> GenerateNonce(
        [FromQuery] string publicKey,
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
            
            if (string.IsNullOrEmpty(publicKey))
            {
                logger.LogInformation("PublicKey was null or empty.");
                return Results.BadRequest(new { error = "Missing publicKey" });
            }

            var nonce = await walletService.GenerateNonce(token.UserId, publicKey);
            logger.LogInformation("Generated nonce for user {UserId} and wallet {PublicKey}", token.UserId, publicKey);

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
                !data.TryGetProperty("Signature", out var signatureProp))
            {
                return Results.BadRequest(new { error = " Missing required fields." });
            }

            var nonce = nonceProp.GetString();
            var publicKey = publicKeyProp.GetString();
            var signature = signatureProp.GetString();
            var token = await tokenService.GetCurrentTokenAsync();
            
            
            var success = await walletService.RegisterWallet(token.UserId, nonce, publicKey, signature);

            return !success
                ? Results.BadRequest(new { error = "Verification failed." })
                : Results.Ok();
        }
        catch (Exception ex)
        {
            return Results.Problem("Internal server error. Please check the server logs.");
        }
    }

    
    [ValourRoute(HttpVerbs.Get, "api/userWallet/verify")]
    public static async Task<IResult> VerifyWallet([FromQuery] string publicKey,
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
            ? Results.BadRequest(new { error = "Verification failed." })
            : Results.Ok(new { success = true });
    }

    
    [ValourRoute(HttpVerbs.Get, "api/userWallet/disconnect")]
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
            ? Results.BadRequest(new { error = "Verification failed." })
            : Results.Ok(new { success = true });
    }

}