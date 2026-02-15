#if ANDROID
using Plugin.Firebase.CloudMessaging;
#endif
#if MACCATALYST
using Foundation;
using UIKit;
using UserNotifications;
#endif
using Valour.Client.Components.Notifications;
using Valour.Client.Notifications;
using Valour.Sdk.Client;
using Valour.Sdk.Models;
using Valour.Shared.Models;

namespace Valour.Client.Maui.Notifications;

public class MauiPushNotificationService : IPushNotificationService
{
    private readonly ValourClient _client;
#if MACCATALYST
    private const string MacCatalystLocalEndpoint = "maccatalyst-local";
#endif

    public MauiPushNotificationService(ValourClient client)
    {
        _client = client;

#if MACCATALYST
        _client.NotificationService.NotificationReceived += OnClientNotificationReceivedAsync;
#endif
    }

    public async Task<PushSubscriptionResult> RequestSubscriptionAsync()
    {
#if MACCATALYST
        try
        {
            var permissionState = await GetPermissionStateAsync();
            if (permissionState == "denied")
            {
                return new PushSubscriptionResult
                {
                    Success = false,
                    Error = "Notification permission denied. Please enable notifications in your system settings."
                };
            }

            if (permissionState != "granted")
            {
                await AskForPermissionAsync();
                permissionState = await GetPermissionStateAsync();
            }

            if (permissionState != "granted")
            {
                return new PushSubscriptionResult
                {
                    Success = false,
                    Error = "Notification permission was not granted."
                };
            }

            Preferences.Set("push_subscribed", true);
            return new PushSubscriptionResult
            {
                Success = true,
                Subscription = new PushSubscriptionDetails
                {
                    Endpoint = MacCatalystLocalEndpoint,
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
                Error = $"Native notification error: {ex.Message}"
            };
        }
#elif !ANDROID
        return await Task.FromResult(new PushSubscriptionResult
        {
            Success = false,
            Error = "Push subscriptions are not yet supported on this platform."
        });
#else
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

            Preferences.Set("push_subscribed", true);

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
#endif
    }

    public async Task UnsubscribeAsync()
    {
#if MACCATALYST
        Preferences.Set("push_subscribed", false);
        UNUserNotificationCenter.Current.RemoveAllPendingNotificationRequests();
        await Task.CompletedTask;
#elif !ANDROID
        Preferences.Set("push_subscribed", false);
        await Task.CompletedTask;
#else
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

            Preferences.Set("push_subscribed", false);
        }
        catch (Exception)
        {
            // Best-effort unsubscribe
        }
#endif
    }

    public async Task<PushSubscriptionResult> GetSubscriptionAsync()
    {
#if MACCATALYST
        if (!Preferences.Get("push_subscribed", false))
        {
            return await Task.FromResult(new PushSubscriptionResult
            {
                Success = false,
                Error = "No native notification subscription found."
            });
        }

        return await Task.FromResult(new PushSubscriptionResult
        {
            Success = true,
            Subscription = new PushSubscriptionDetails
            {
                Endpoint = MacCatalystLocalEndpoint,
                Key = "",
                Auth = "",
            }
        });
#elif !ANDROID
        return await Task.FromResult(new PushSubscriptionResult
        {
            Success = false,
            Error = "Push subscriptions are not yet supported on this platform."
        });
#else
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
#endif
    }

    public Task<bool> IsNotificationsEnabledAsync()
    {
        return Task.FromResult(Preferences.Get("push_subscribed", false));
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
#elif MACCATALYST
        var settings = await UNUserNotificationCenter.Current.GetNotificationSettingsAsync();
        return settings.AuthorizationStatus switch
        {
            UNAuthorizationStatus.Authorized => "granted",
            UNAuthorizationStatus.Provisional => "granted",
            UNAuthorizationStatus.Denied => "denied",
            _ => "default"
        };
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
#elif MACCATALYST
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            await UNUserNotificationCenter.Current.RequestAuthorizationAsync(
                UNAuthorizationOptions.Alert |
                UNAuthorizationOptions.Sound |
                UNAuthorizationOptions.Badge
            );
        });
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
#elif MACCATALYST
        var settingsUrl = new NSUrl(UIApplication.OpenSettingsUrlString);
        if (UIApplication.SharedApplication.CanOpenUrl(settingsUrl))
        {
            UIApplication.SharedApplication.OpenUrl(settingsUrl);
        }
#endif
        return Task.CompletedTask;
    }

#if MACCATALYST
    private Task OnClientNotificationReceivedAsync(Notification notification)
    {
        if (!Preferences.Get("push_subscribed", false))
            return Task.CompletedTask;

        var title = string.IsNullOrWhiteSpace(notification.Title) ? "Valour" : notification.Title;
        var body = notification.Body ?? string.Empty;

        _ = MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var settings = await UNUserNotificationCenter.Current.GetNotificationSettingsAsync();
            if (settings.AuthorizationStatus is not (UNAuthorizationStatus.Authorized or UNAuthorizationStatus.Provisional))
                return;

            var content = new UNMutableNotificationContent
            {
                Title = title,
                Body = body,
                Sound = UNNotificationSound.Default
            };

            if (!string.IsNullOrWhiteSpace(notification.ClickUrl))
            {
                content.UserInfo = NSDictionary<NSString, NSObject>.FromObjectAndKey(
                    new NSString(notification.ClickUrl),
                    new NSString("url")
                );
            }

            var request = UNNotificationRequest.FromIdentifier(
                $"valour-{notification.Id}",
                content,
                null
            );

            await UNUserNotificationCenter.Current.AddNotificationRequestAsync(request);
        });

        return Task.CompletedTask;
    }
#endif
}
