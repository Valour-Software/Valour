using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.Content;

namespace Valour.Client.Maui;

/// <summary>
/// Keeps a voice call alive while the app is in the background. The service
/// uses the microphone type, which is what Android and Google Play expect for
/// an app that keeps capturing call audio in the background. Android 14 and
/// later refuse to start a microphone service without the microphone
/// permission, so a call joined without it does not start the service.
/// </summary>
[Service(ForegroundServiceType = ForegroundService.TypeMicrophone)]
public class CallForegroundService : Service
{
    private const int NotificationId = 9002;
    private const string ChannelId = "valour_call";

    private PowerManager.WakeLock? _wakeLock;

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        CreateNotificationChannel();

        var builder = OperatingSystem.IsAndroidVersionAtLeast(26)
            ? new Notification.Builder(this, ChannelId)
            : new Notification.Builder(this);

        var notification = builder
            .SetContentTitle("Valour")
            .SetContentText("Voice call in progress")
            .SetSmallIcon(Resource.Mipmap.appicon)
            .SetOngoing(true)
            .Build();

        try
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(29))
                StartForeground(NotificationId, notification, ForegroundService.TypeMicrophone);
            else
                StartForeground(NotificationId, notification);
        }
        catch (Exception)
        {
            // Android refuses a microphone service started from the
            // background, for example when a call reconnects while the app is
            // hidden. The call continues without the keep-alive.
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        AcquireWakeLock();

        return StartCommandResult.Sticky;
    }

    public override void OnDestroy()
    {
        ReleaseWakeLock();
        base.OnDestroy();
    }

    private void AcquireWakeLock()
    {
        if (_wakeLock is not null)
            return;

        var powerManager = (PowerManager?)GetSystemService(PowerService);
        if (powerManager is null)
            return;

        var wakeLock = powerManager.NewWakeLock(WakeLockFlags.Partial, "Valour::VoiceCall");
        if (wakeLock is null)
            return;

        wakeLock.Acquire();
        _wakeLock = wakeLock;
    }

    private void ReleaseWakeLock()
    {
        if (_wakeLock is null)
            return;

        if (_wakeLock.IsHeld)
            _wakeLock.Release();

        _wakeLock = null;
    }

    private void CreateNotificationChannel()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
            return;

        var channel = new NotificationChannel(ChannelId, "Voice Call", NotificationImportance.Low)
        {
            Description = "Keeps voice call active in the background"
        };

        var manager = (NotificationManager?)GetSystemService(NotificationService);
        manager?.CreateNotificationChannel(channel);
    }

    public static void Start()
    {
        var activity = Platform.CurrentActivity;
        if (activity is null)
            return;

        if (ContextCompat.CheckSelfPermission(activity, Manifest.Permission.RecordAudio) != Permission.Granted)
            return;

        var intent = new Intent(activity, typeof(CallForegroundService));

        if (OperatingSystem.IsAndroidVersionAtLeast(26))
            activity.StartForegroundService(intent);
        else
            activity.StartService(intent);
    }

    public static void Stop()
    {
        var activity = Platform.CurrentActivity;
        if (activity is null)
            return;

        var intent = new Intent(activity, typeof(CallForegroundService));
        activity.StopService(intent);
    }
}
