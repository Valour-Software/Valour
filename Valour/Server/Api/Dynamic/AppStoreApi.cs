using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Valour.Config.Configs;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Server.Api.Dynamic;

public class AppStoreApi
{
    /// <summary>
    /// The iOS app reports a subscription purchase here after App Store
    /// checkout completes, and again on startup or restore for any transaction
    /// it still holds.
    /// </summary>
    [ValourRoute(HttpVerbs.Post, "api/app-store/subscriptions")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> ClaimSubscriptionAsync(
        [FromBody] AppStorePurchaseRequest request,
        UserService userService,
        AppStoreBillingService billingService,
        ILogger<AppStoreApi> logger)
    {
        if (string.IsNullOrWhiteSpace(request?.TransactionId))
            return ValourResult.BadRequest("Missing transaction ID.");

        var userId = await userService.GetCurrentUserIdAsync();
        return await ClaimAsync(() => billingService.ClaimSubscriptionAsync(userId, request.TransactionId), logger);
    }

    /// <summary>
    /// The iOS app reports a Valour Credits purchase here, and finishes the
    /// StoreKit transaction once this succeeds.
    /// </summary>
    [ValourRoute(HttpVerbs.Post, "api/app-store/credits")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> ClaimCreditsAsync(
        [FromBody] AppStorePurchaseRequest request,
        UserService userService,
        AppStoreBillingService billingService,
        ILogger<AppStoreApi> logger)
    {
        if (string.IsNullOrWhiteSpace(request?.TransactionId) || string.IsNullOrWhiteSpace(request.ProductId))
            return ValourResult.BadRequest("Missing purchase details.");

        var userId = await userService.GetCurrentUserIdAsync();
        return await ClaimAsync(() => billingService.ClaimCreditsAsync(userId, request.ProductId, request.TransactionId), logger);
    }

    /// <summary>
    /// Runs a claim and turns a failure to reach the App Store into a message
    /// the app can show. The app keeps the transaction unfinished and reports
    /// it again later, so the purchase still arrives.
    /// </summary>
    private static async Task<IResult> ClaimAsync(Func<Task<TaskResult>> claim, ILogger logger)
    {
        try
        {
            return Results.Json(await claim());
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            logger.LogError(e, "Could not reach the App Store Server API");
            return Results.Json(TaskResult.FromFailure(
                "The App Store could not confirm the purchase right now. It will be added to your account automatically."));
        }
    }

    /// <summary>
    /// Receives App Store Server Notifications (version 2). The notification
    /// URL in App Store Connect carries the configured token. Payloads only
    /// name a transaction; its state is always read back from the App Store.
    /// </summary>
    [ValourRoute(HttpVerbs.Post, "api/app-store/notifications")]
    public static async Task<IResult> NotificationAsync(
        HttpContext httpContext,
        AppStoreBillingService billingService,
        ILogger<AppStoreApi> logger)
    {
        var expected = AppStoreConfig.Current?.NotificationToken;
        if (!AppStoreBillingService.IsConfigured || string.IsNullOrWhiteSpace(expected))
            return Results.NotFound();

        var provided = httpContext.Request.Query["token"].FirstOrDefault() ?? "";
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(expected)))
            return Results.Unauthorized();

        string signedPayload;
        try
        {
            var body = await JsonSerializer.DeserializeAsync<NotificationBody>(httpContext.Request.Body);
            signedPayload = body?.SignedPayload;
        }
        catch (JsonException e)
        {
            // Acknowledge malformed messages so Apple does not redeliver them.
            logger.LogWarning(e, "Ignoring malformed App Store notification");
            return Results.Ok();
        }

        if (string.IsNullOrEmpty(signedPayload))
            return Results.Ok();

        try
        {
            await billingService.HandleNotificationAsync(signedPayload);
        }
        catch (Exception e)
        {
            // A non-success status makes Apple retry the delivery.
            logger.LogError(e, "Failed to handle App Store notification");
            return Results.Problem("Notification processing failed.", statusCode: StatusCodes.Status500InternalServerError);
        }

        return Results.Ok();
    }

    private class NotificationBody
    {
        [JsonPropertyName("signedPayload")]
        public string SignedPayload { get; set; }
    }
}
