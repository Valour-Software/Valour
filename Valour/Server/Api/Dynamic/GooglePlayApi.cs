using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google;
using Microsoft.AspNetCore.Mvc;
using Valour.Config.Configs;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Server.Api.Dynamic;

public class GooglePlayApi
{
    /// <summary>
    /// The Android app reports a subscription purchase here after Google Play
    /// checkout completes, and again on startup for any purchase it still holds.
    /// </summary>
    [ValourRoute(HttpVerbs.Post, "api/google-play/subscriptions")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> ClaimSubscriptionAsync(
        [FromBody] GooglePlayPurchaseRequest request,
        UserService userService,
        GooglePlayBillingService billingService,
        ILogger<GooglePlayApi> logger)
    {
        if (string.IsNullOrWhiteSpace(request?.PurchaseToken))
            return ValourResult.BadRequest("Missing purchase token.");

        var userId = await userService.GetCurrentUserIdAsync();
        return await ClaimAsync(() => billingService.ClaimSubscriptionAsync(userId, request.PurchaseToken), logger);
    }

    /// <summary>
    /// The Android app reports a Valour Credits purchase here.
    /// </summary>
    [ValourRoute(HttpVerbs.Post, "api/google-play/credits")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> ClaimCreditsAsync(
        [FromBody] GooglePlayPurchaseRequest request,
        UserService userService,
        GooglePlayBillingService billingService,
        ILogger<GooglePlayApi> logger)
    {
        if (string.IsNullOrWhiteSpace(request?.PurchaseToken) || string.IsNullOrWhiteSpace(request.ProductId))
            return ValourResult.BadRequest("Missing purchase details.");

        var userId = await userService.GetCurrentUserIdAsync();
        return await ClaimAsync(() => billingService.ClaimCreditsAsync(userId, request.ProductId, request.PurchaseToken), logger);
    }

    /// <summary>
    /// Runs a claim and turns a failure to reach Google Play into a message the
    /// app can show. Google's notification for the same purchase is retried
    /// until it succeeds, so the purchase still arrives.
    /// </summary>
    private static async Task<IResult> ClaimAsync(Func<Task<TaskResult>> claim, ILogger logger)
    {
        try
        {
            return Results.Json(await claim());
        }
        catch (GoogleApiException e)
        {
            logger.LogError(e, "Google Play rejected a purchase lookup");
            return Results.Json(TaskResult.FromFailure(
                "Google Play could not confirm the purchase right now. It will be added to your account automatically."));
        }
    }

    /// <summary>
    /// Receives Google Play real-time developer notifications through a Pub/Sub
    /// push subscription. The push URL carries the configured token. Payloads
    /// only name a purchase; its state is always read back from Google Play.
    /// </summary>
    [ValourRoute(HttpVerbs.Post, "api/google-play/notifications")]
    public static async Task<IResult> NotificationAsync(
        HttpContext httpContext,
        GooglePlayBillingService billingService,
        ILogger<GooglePlayApi> logger)
    {
        var expected = GooglePlayConfig.Current?.NotificationToken;
        if (!GooglePlayBillingService.IsConfigured || string.IsNullOrWhiteSpace(expected))
            return Results.NotFound();

        var provided = httpContext.Request.Query["token"].FirstOrDefault() ?? "";
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(expected)))
            return Results.Unauthorized();

        GooglePlayNotification notification;
        try
        {
            var envelope = await JsonSerializer.DeserializeAsync<PubSubPushEnvelope>(httpContext.Request.Body);
            var data = Convert.FromBase64String(envelope?.Message?.Data ?? "");
            notification = JsonSerializer.Deserialize<GooglePlayNotification>(data);
        }
        catch (Exception e) when (e is JsonException or FormatException)
        {
            // Acknowledge malformed messages so Pub/Sub does not redeliver them forever.
            logger.LogWarning(e, "Ignoring malformed Google Play notification");
            return Results.Ok();
        }

        if (notification is null)
            return Results.Ok();

        try
        {
            await billingService.HandleNotificationAsync(notification);
        }
        catch (Exception e)
        {
            // A non-success status makes Pub/Sub retry the delivery.
            logger.LogError(e, "Failed to handle Google Play notification");
            return Results.Problem("Notification processing failed.", statusCode: StatusCodes.Status500InternalServerError);
        }

        return Results.Ok();
    }

    private class PubSubPushEnvelope
    {
        [JsonPropertyName("message")]
        public PubSubMessage Message { get; set; }
    }

    private class PubSubMessage
    {
        [JsonPropertyName("data")]
        public string Data { get; set; }
    }
}
