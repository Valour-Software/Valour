using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace Valour.Client.Maui;

[Service(ForegroundServiceType = ForegroundService.TypeMediaPlayback)]
public class CallForegroundService : Service
{
    private const int NotificationId = 9002;
    private const string ChannelId = "valour_call";

    private PowerManager.WakeLock? _wakeLock;

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        CreateNotificationChannel();

        var notification = new Notification.Builder(this, ChannelId)
            .SetContentTitle("Valour")
            .SetContentText("Voice call in progress")
            .SetSmallIcon(Resource.Mipmap.appicon)
            .SetOngoing(true)
            .Build();

        if (OperatingSystem.IsAndroidVersionAtLeast(29))
            StartForeground(NotificationId, notification, ForegroundService.TypeMediaPlayback);
        else
            StartForeground(NotificationId, notification);

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

        _wakeLock = powerManager.NewWakeLock(WakeLockFlags.Partial, "Valour::VoiceCall");
        _wakeLock.Acquire();
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
        if (Build.VERSION.SdkInt < BuildVersionCodes.O)
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
