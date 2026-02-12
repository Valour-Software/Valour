using Microsoft.JSInterop;
using Valour.Client.Components.Notifications;
using Valour.Sdk.Client;
using Valour.Sdk.Models;
using Valour.Shared.Models;

namespace Valour.Client.Notifications;

public class BrowserPushNotificationService : IPushNotificationService
{
    private readonly IJSRuntime _jsRuntime;
    private readonly ValourClient _client;

    private IJSObjectReference? _jsModule;
    private IJSObjectReference? _jsService;
    private bool _initialized;

    public BrowserPushNotificationService(IJSRuntime jsRuntime, ValourClient client)
    {
        _jsRuntime = jsRuntime;
        _client = client;
    }

    private async Task EnsureInitializedAsync()
    {
        if (_initialized)
            return;

        _jsModule = await _jsRuntime.InvokeAsync<IJSObjectReference>(
            "import",
            "./_content/Valour.Client/Components/Notifications/PushSubscriptionsComponent.razor.js");

        _jsService = await _jsModule.InvokeAsync<IJSObjectReference>("init");
        _initialized = true;
    }

    public async Task<PushSubscriptionResult> RequestSubscriptionAsync()
    {
        await EnsureInitializedAsync();

        var subResult = await _jsService!.InvokeAsync<PushSubscriptionResult>("requestSubscription");

        if (!subResult.Success)
            return subResult;

        if (subResult.Subscription is null)
        {
            return new PushSubscriptionResult
            {
                Success = false,
                Error = "Unknown subscription error"
            };
        }

        var pushNotificationSubscription = new PushNotificationSubscription
        {
            UserId = _client.Me.Id,
            Key = subResult.Subscription.Key,
            Auth = subResult.Subscription.Auth,
            Endpoint = subResult.Subscription.Endpoint,
            DeviceType = NotificationDeviceType.WebPush,
        };

        var result = await _client.PrimaryNode.PostAsync("api/notifications/subscribe", pushNotificationSubscription);

        if (!result.Success)
        {
            return new PushSubscriptionResult
            {
                Success = false,
                Error = result.Message
            };
        }

        return subResult;
    }

    public async Task UnsubscribeAsync()
    {
        await EnsureInitializedAsync();

        var subResult = await GetSubscriptionAsync();
        if (subResult.Success && subResult.Subscription is not null)
        {
            var pushNotificationSubscription = new PushNotificationSubscription
            {
                UserId = _client.Me.Id,
                Key = subResult.Subscription.Key,
                Auth = subResult.Subscription.Auth,
                Endpoint = subResult.Subscription.Endpoint,
                DeviceType = NotificationDeviceType.WebPush,
            };

            await _jsService!.InvokeVoidAsync("unsubscribe");
            await _client.PrimaryNode.PostAsync("api/notifications/unsubscribe", pushNotificationSubscription);
        }
    }

    public async Task<PushSubscriptionResult> GetSubscriptionAsync()
    {
        await EnsureInitializedAsync();
        return await _jsService!.InvokeAsync<PushSubscriptionResult>("getSubscription");
    }

    public async Task<bool> IsNotificationsEnabledAsync()
    {
        await EnsureInitializedAsync();
        return await _jsService!.InvokeAsync<bool>("notificationsEnabled");
    }

    public async Task<string> GetPermissionStateAsync()
    {
        await EnsureInitializedAsync();
        return await _jsService!.InvokeAsync<string>("getPermissionState");
    }

    public async Task AskForPermissionAsync()
    {
        await EnsureInitializedAsync();
        await _jsService!.InvokeVoidAsync("askForPermission");
    }
}
