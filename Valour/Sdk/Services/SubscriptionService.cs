using Valour.Sdk.Client;
using Valour.Shared;

namespace Valour.SDK.Services;

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
        var result = await _client.PrimaryNode.GetJsonAsync<UserSubscription>($"api/subscriptions/active/{_client.Self.Id}", true);
        return result.Data;
    }
}