using Valour.Sdk.Client;
using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Sdk.Services;

public class SubscriptionService
{
    private readonly ValourClient _client;
    
    public SubscriptionService(ValourClient client)
    {
        _client = client;
    }
    
    /// <summary>
    /// Subscribe to Stargazer Plus! (...or Premium? What are we even calling it???)
    /// </summary>
    public async Task<TaskResult> SubscribeAsync(string type)
    {
        var result = await _client.PrimaryNode.PostAsyncWithResponse<TaskResult>($"api/subscriptions/{type}/start");
        if (!result.Success)
        {
            return new TaskResult(false, result.Message);
        }

        return result.Data;
    }
    
    /// <summary>
    /// Unsubscribe (sobs quietly in the corner)
    /// </summary>
    public async Task<TaskResult> UnsubscribeAsync()
    {
        var result = await _client.PrimaryNode.PostAsyncWithResponse<TaskResult>($"api/subscriptions/end");
        if (!result.Success)
        {
            return new TaskResult(false, result.Message);
        }

        return result.Data;
    }
    
    public async Task<decimal> GetSubscriptionPriceAsync(string type)
    {
        var result = await _client.PrimaryNode.GetJsonAsync<decimal>($"api/subscriptions/{type}/price");
        return result.Data;
    }

    public async Task<UserSubscription> GetActiveSubscriptionAsync()
    {
        var result = await _client.PrimaryNode.GetJsonAsync<UserSubscription>($"api/subscriptions/active", true);
        return result.Data;
    }

    /// <summary>
    /// Cancel a Stripe-managed subscription (cancels at period end)
    /// </summary>
    public async Task<TaskResult> CancelStripeSubscriptionAsync()
    {
        return await _client.PrimaryNode.PostAsync("api/stripe/subscriptions/cancel", (string)null);
    }

    /// <summary>
    /// Cancel a pending tier change (downgrade scheduled for next cycle)
    /// </summary>
    public async Task<TaskResult> CancelPendingChangeAsync()
    {
        var result = await _client.PrimaryNode.PostAsyncWithResponse<TaskResult>("api/subscriptions/cancel-pending");
        if (!result.Success)
            return new TaskResult(false, result.Message);
        return result.Data;
    }

    /// <summary>
    /// Change a Stripe subscription to a different tier (upgrade or downgrade)
    /// </summary>
    public async Task<TaskResult> ChangeStripeSubscriptionAsync(string tierName)
    {
        var result = await _client.PrimaryNode.PostAsyncWithResponse<StripeChangeResult>($"api/stripe/subscriptions/change/{tierName}");
        if (!result.Success)
            return new TaskResult(false, result.Message);
        return new TaskResult(result.Data?.Success ?? false, result.Data?.Message);
    }

    /// <summary>
    /// Reports a Google Play subscription purchase so the server can verify and record it
    /// </summary>
    public async Task<TaskResult> ClaimGooglePlaySubscriptionAsync(string productId, string purchaseToken)
    {
        var result = await _client.PrimaryNode.PostAsyncWithResponse<TaskResult>("api/google-play/subscriptions",
            new GooglePlayPurchaseRequest { ProductId = productId, PurchaseToken = purchaseToken });
        if (!result.Success)
            return new TaskResult(false, result.Message);
        return result.Data;
    }

    /// <summary>
    /// Reports a Google Play Valour Credits purchase so the server can verify it and deposit the credits
    /// </summary>
    public async Task<TaskResult> ClaimGooglePlayCreditsAsync(string productId, string purchaseToken)
    {
        var result = await _client.PrimaryNode.PostAsyncWithResponse<TaskResult>("api/google-play/credits",
            new GooglePlayPurchaseRequest { ProductId = productId, PurchaseToken = purchaseToken });
        if (!result.Success)
            return new TaskResult(false, result.Message);
        return result.Data;
    }

    private class StripeChangeResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
    }
}