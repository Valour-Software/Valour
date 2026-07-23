#if ANDROID
using Plugin.Firebase.CloudMessaging;
#endif
using Valour.Client.Components.Notifications;
using Valour.Client.Notifications;
using Valour.Sdk.Client;
using Valour.Sdk.Models;
using Valour.Shared.Models;

namespace Valour.Client.Maui.Notifications;

#if ANDROID
public class MauiPushNotificationService : IPushNotificationService
{
    private const string PushSubscribedPreferenceKey = "push_subscribed";
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

            Preferences.Default.Set(PushSubscribedPreferenceKey, true);

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
        finally
        {
            Preferences.Default.Set(PushSubscribedPreferenceKey, false);
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
        // Reflects user intent (local opt-in) plus OS permission. Deliberately does NOT
        // consult the server: server-side subscriptions expire and are purged, and gating
        // re-subscription on the server's answer meant an expired row could never be
        // renewed, permanently killing push for the device.
        var locallySubscribed = Preferences.Default.Get(PushSubscribedPreferenceKey, false);
        if (!locallySubscribed)
            return false;

        try
        {
            // Don't clear the local preference on denial - if the user re-grants
            // permission in OS settings, the next app open will re-subscribe.
            var permissionState = await GetPermissionStateAsync();
            return permissionState != "denied";
        }
        catch (Exception)
        {
            return locallySubscribed;
        }
    }

    public async Task<string> GetPermissionStateAsync()
    {
#if ANDROID
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            try
            {
                var status = await Permissions.CheckStatusAsync<Permissions.PostNotifications>();
                return status switch
                {
                    PermissionStatus.Granted => "granted",
                    PermissionStatus.Denied => "denied",
                    _ => "default"
                };
            }
            catch (Exception)
            {
                // A failed permission check must never take down callers
                // (settings UI, keepalive). Treat as undetermined.
                return "default";
            }
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
            try
            {
                await Permissions.RequestAsync<Permissions.PostNotifications>();
            }
            catch (Exception)
            {
                // Best-effort; RequestSubscriptionAsync re-checks the state after
            }
        }
#endif
    }

    public Task OpenNotificationSettingsAsync()
    {
#if ANDROID
        var context = Platform.CurrentActivity ?? Platform.AppContext;
        var intent = OperatingSystem.IsAndroidVersionAtLeast(26)
            ? new Android.Content.Intent(Android.Provider.Settings.ActionAppNotificationSettings)
            : new Android.Content.Intent(Android.Provider.Settings.ActionSettings);

        if (OperatingSystem.IsAndroidVersionAtLeast(26))
            intent.PutExtra(Android.Provider.Settings.ExtraAppPackage, context.PackageName);

        intent.AddFlags(Android.Content.ActivityFlags.NewTask);
        context.StartActivity(intent);
#endif
        return Task.CompletedTask;
    }

    public Task DismissNotificationAsync(Guid notificationId, long? sourceId) => Task.CompletedTask;

    public Task DismissAllNotificationsAsync() => Task.CompletedTask;
}
#elif WINDOWS
public class MauiPushNotificationService : IPushNotificationService
{
    private readonly WindowsToastService _toastService;

    public MauiPushNotificationService(WindowsToastService toastService)
    {
        _toastService = toastService;
    }

    public Task<PushSubscriptionResult> RequestSubscriptionAsync()
    {
        _toastService.Enable();
        Preferences.Set("push_subscribed", true);

        return Task.FromResult(new PushSubscriptionResult
        {
            Success = true,
            Subscription = new PushSubscriptionDetails
            {
                Endpoint = "windows-local",
                Key = "",
                Auth = "",
            }
        });
    }

    public Task UnsubscribeAsync()
    {
        _toastService.Disable();
        Preferences.Set("push_subscribed", false);
        return Task.CompletedTask;
    }

    public Task<PushSubscriptionResult> GetSubscriptionAsync()
    {
        if (Preferences.Get("push_subscribed", false))
        {
            return Task.FromResult(new PushSubscriptionResult
            {
                Success = true,
                Subscription = new PushSubscriptionDetails
                {
                    Endpoint = "windows-local",
                    Key = "",
                    Auth = "",
                }
            });
        }

        return Task.FromResult(new PushSubscriptionResult
        {
            Success = false,
            Error = "Notifications not enabled"
        });
    }

    public Task<bool> IsNotificationsEnabledAsync()
    {
        return Task.FromResult(Preferences.Get("push_subscribed", false));
    }

    public Task<string> GetPermissionStateAsync() => Task.FromResult("granted");

    public Task AskForPermissionAsync() => Task.CompletedTask;

    public Task OpenNotificationSettingsAsync() => Task.CompletedTask;

    public Task DismissNotificationAsync(Guid notificationId, long? sourceId) => Task.CompletedTask;

    public Task DismissAllNotificationsAsync() => Task.CompletedTask;
}
#else
public class MauiPushNotificationService : IPushNotificationService
{
    public Task<PushSubscriptionResult> RequestSubscriptionAsync() =>
        Task.FromResult(new PushSubscriptionResult { Success = false, Error = "Push notifications are not supported on this platform." });

    public Task UnsubscribeAsync() => Task.CompletedTask;

    public Task<PushSubscriptionResult> GetSubscriptionAsync() =>
        Task.FromResult(new PushSubscriptionResult { Success = false, Error = "Push notifications are not supported on this platform." });

    public Task<bool> IsNotificationsEnabledAsync() => Task.FromResult(false);

    public Task<string> GetPermissionStateAsync() => Task.FromResult("denied");

    public Task AskForPermissionAsync() => Task.CompletedTask;

    public Task OpenNotificationSettingsAsync() => Task.CompletedTask;

    public Task DismissNotificationAsync(Guid notificationId, long? sourceId) => Task.CompletedTask;

    public Task DismissAllNotificationsAsync() => Task.CompletedTask;
}
#endif
