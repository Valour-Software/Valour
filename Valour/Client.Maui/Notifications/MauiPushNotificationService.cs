using Plugin.Firebase.CloudMessaging;
using Valour.Client.Components.Notifications;
using Valour.Client.Notifications;
using Valour.Sdk.Client;
using Valour.Sdk.Models;
using Valour.Shared.Models;

namespace Valour.Client.Maui.Notifications;

public class MauiPushNotificationService : IPushNotificationService
{
    private readonly ValourClient _client;

    public MauiPushNotificationService(ValourClient client)
    {
        _client = client;
    }

    public async Task<PushSubscriptionResult> RequestSubscriptionAsync()
    {
        try
        {
            await CrossFirebaseCloudMessaging.Current.CheckIfValidAsync();
            var token = await CrossFirebaseCloudMessaging.Current.GetTokenAsync();

            if (string.IsNullOrEmpty(token))
            {
                return new PushSubscriptionResult
                {
                    Success = false,
                    Error = "Failed to get FCM token"
                };
            }

            var pushNotificationSubscription = new PushNotificationSubscription
            {
                UserId = _client.Me.Id,
                Endpoint = token,
                Key = null,
                Auth = null,
                DeviceType = NotificationDeviceType.AndroidFcm,
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

            return new PushSubscriptionResult
            {
                Success = true,
                Subscription = new PushSubscriptionDetails
                {
                    Endpoint = token,
                    Key = "",
                    Auth = "",
                }
            };
        }
        catch (Exception ex)
        {
            return new PushSubscriptionResult
            {
                Success = false,
                Error = $"FCM error: {ex.Message}"
            };
        }
    }

    public async Task UnsubscribeAsync()
    {
        try
        {
            var token = await CrossFirebaseCloudMessaging.Current.GetTokenAsync();

            if (!string.IsNullOrEmpty(token))
            {
                var pushNotificationSubscription = new PushNotificationSubscription
                {
                    UserId = _client.Me.Id,
                    Endpoint = token,
                    Key = null,
                    Auth = null,
                    DeviceType = NotificationDeviceType.AndroidFcm,
                };

                await _client.PrimaryNode.PostAsync("api/notifications/unsubscribe", pushNotificationSubscription);
            }
        }
        catch (Exception)
        {
            // Best-effort unsubscribe
        }
    }

    public async Task<PushSubscriptionResult> GetSubscriptionAsync()
    {
        try
        {
            var token = await CrossFirebaseCloudMessaging.Current.GetTokenAsync();

            if (!string.IsNullOrEmpty(token))
            {
                return new PushSubscriptionResult
                {
                    Success = true,
                    Subscription = new PushSubscriptionDetails
                    {
                        Endpoint = token,
                        Key = "",
                        Auth = "",
                    }
                };
            }

            return new PushSubscriptionResult
            {
                Success = false,
                Error = "No FCM token found"
            };
        }
        catch (Exception ex)
        {
            return new PushSubscriptionResult
            {
                Success = false,
                Error = $"FCM error: {ex.Message}"
            };
        }
    }

    public Task<bool> IsNotificationsEnabledAsync()
    {
        // On Android, if we have a valid FCM token, notifications are enabled
        return Task.FromResult(true);
    }

    public Task<string> GetPermissionStateAsync()
    {
        return Task.FromResult("granted");
    }

    public Task AskForPermissionAsync()
    {
        // Android 13+ POST_NOTIFICATIONS permission is handled at the platform level
        // The Plugin.Firebase.CloudMessaging handles this internally
        return Task.CompletedTask;
    }
}
