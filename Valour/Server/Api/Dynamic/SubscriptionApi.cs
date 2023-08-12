using Valour.Shared.Authorization;

namespace Valour.Server.Api.Dynamic;

public class SubscriptionApi
{
    [ValourRoute(HttpVerbs.Get, "api/subscriptions/active/{userId}")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> GetActiveAsync(
        UserService userService, 
        SubscriptionService subService)
    {
        var userId = await userService.GetCurrentUserIdAsync();
        var result = await subService.GetActiveSubscriptionAsync(userId);
        if (result is null)
            return ValourResult.NotFound("No active subscription found.");

        return Results.Json(result);
    }
    
    [ValourRoute(HttpVerbs.Get, "api/subscriptions/{subType}/price")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> GetPriceAsync(
        string subType, 
        UserService userService, 
        SubscriptionService subService)
    {
        var userId = await userService.GetCurrentUserIdAsync();
        var result = await subService.GetSubscriptionPriceAsync(userId, subType);

        return Results.Json(result);
    }
    
    [ValourRoute(HttpVerbs.Post, "api/subscriptions/{subType}/start")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> SubscribeAsync(
        string subType, 
        UserService userService, 
        SubscriptionService subService)
    {
        var userId = await userService.GetCurrentUserIdAsync();
        var result = await subService.StartSubscriptionAsync(userId, subType);

        return Results.Json(result);
    }
    
    [ValourRoute(HttpVerbs.Post, "api/subscriptions/end")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> UnsubscribeAsync(
        UserService userService, 
        SubscriptionService subService)
    {
        var userId = await userService.GetCurrentUserIdAsync();
        var result = await subService.EndSubscriptionAsync(userId);

        return Results.Json(result);
    }
}