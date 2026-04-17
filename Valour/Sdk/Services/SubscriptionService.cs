using Valour.Sdk.Client;
using Valour.Shared;

namespace Valour.Sdk.Services;

public class SubscriptionService
{
    private readonly ValourClient _client;
    
    public SubscriptionService(ValourClient client)
    {
        _client = client;
    }
    
    /// <summary>
    /// Subscribe to Valour Plus! (...or Premium? What are we even calling it???)
    /// </summary>
    public async Task<TaskResult> SubscribeAsync(string type)
    {
        var result = await _client.AccountNode.PostAsyncWithResponse<TaskResult>($"api/subscriptions/{type}/start");
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
        var result = await _client.AccountNode.PostAsyncWithResponse<TaskResult>($"api/subscriptions/end");
        if (!result.Success)
        {
            return new TaskResult(false, result.Message);
        }

        return result.Data;
    }
    
    public async Task<decimal> GetSubscriptionPriceAsync(string type)
    {
        var result = await _client.AccountNode.GetJsonAsync<decimal>($"api/subscriptions/{type}/price");
        return result.Data;
    }

    public async Task<UserSubscription> GetActiveSubscriptionAsync()
    {
        var result = await _client.AccountNode.GetJsonAsync<UserSubscription>($"api/subscriptions/active", true);
        return result.Data;
    }

    /// <summary>
    /// Cancel a Stripe-managed subscription (cancels at period end)
    /// </summary>
    public async Task<TaskResult> CancelStripeSubscriptionAsync()
    {
        return await _client.AccountNode.PostAsync("api/stripe/subscriptions/cancel", (string)null);
    }

    /// <summary>
    /// Cancel a pending tier change (downgrade scheduled for next cycle)
    /// </summary>
    public async Task<TaskResult> CancelPendingChangeAsync()
    {
        var result = await _client.AccountNode.PostAsyncWithResponse<TaskResult>("api/subscriptions/cancel-pending");
        if (!result.Success)
            return new TaskResult(false, result.Message);
        return result.Data;
    }

    /// <summary>
    /// Change a Stripe subscription to a different tier (upgrade or downgrade)
    /// </summary>
    public async Task<TaskResult> ChangeStripeSubscriptionAsync(string tierName)
    {
        var result = await _client.AccountNode.PostAsyncWithResponse<StripeChangeResult>($"api/stripe/subscriptions/change/{tierName}");
        if (!result.Success)
            return new TaskResult(false, result.Message);
        return new TaskResult(result.Data?.Success ?? false, result.Data?.Message);
    }

    private class StripeChangeResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
    }
}
