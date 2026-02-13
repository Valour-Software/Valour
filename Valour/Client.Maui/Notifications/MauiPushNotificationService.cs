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
            // Ensure we have notification permission before subscribing
            var permissionState = await GetPermissionStateAsync();
            if (permissionState == "denied")
            {
                return new PushSubscriptionResult
                {
                    Success = false,
                    Error = "Notification permission denied. Please enable notifications in your device settings."
                };
            }

            if (permissionState != "granted")
            {
                await AskForPermissionAsync();

                permissionState = await GetPermissionStateAsync();
                if (permissionState != "granted")
                {
                    return new PushSubscriptionResult
                    {
                        Success = false,
                        Error = "Notification permission was not granted."
                    };
                }
            }

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
                Key = "",
                Auth = "",
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
                    Key = "",
                    Auth = "",
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

    public async Task<bool> IsNotificationsEnabledAsync()
    {
        var state = await GetPermissionStateAsync();
        return state == "granted";
    }

    public async Task<string> GetPermissionStateAsync()
    {
#if ANDROID
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            var status = await Permissions.CheckStatusAsync<Permissions.PostNotifications>();
            return status switch
            {
                PermissionStatus.Granted => "granted",
                PermissionStatus.Denied => "denied",
                _ => "default"
            };
        }

        // Pre-Android 13: notifications are allowed by default
        return "granted";
#else
        return "granted";
#endif
    }

    public async Task AskForPermissionAsync()
    {
#if ANDROID
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            await Permissions.RequestAsync<Permissions.PostNotifications>();
        }
#endif
    }

    public Task OpenNotificationSettingsAsync()
    {
#if ANDROID
        var context = Platform.CurrentActivity ?? Platform.AppContext;
        var intent = new Android.Content.Intent(Android.Provider.Settings.ActionAppNotificationSettings);
        intent.PutExtra(Android.Provider.Settings.ExtraAppPackage, context.PackageName);
        intent.AddFlags(Android.Content.ActivityFlags.NewTask);
        context.StartActivity(intent);
#endif
        return Task.CompletedTask;
    }
}
