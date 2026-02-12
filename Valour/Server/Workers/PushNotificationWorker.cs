using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using Valour.Config.Configs;
using ThreadChannel = System.Threading.Channels.Channel;

namespace Valour.Server.Workers;

public class PushNotificationAction { }

public class PushNotificationSubscribe : PushNotificationAction
{
    public PushNotificationSubscription Subscription;
}

public class PushNotificationUnsubscribe : PushNotificationAction
{
    public PushNotificationSubscription Subscription;
}

public class SendRolePushNotification : PushNotificationAction
{
    public long RoleId;
    public NotificationContent Content;
}

public class SendUserPushNotification : PushNotificationAction
{
    public long UserId;
    public NotificationContent Content;
}

public class SendMemberPushNotification : PushNotificationAction
{
    public long MemberId;
    public NotificationContent Content;
}

public class QueueRoleMentionNotification : PushNotificationAction
{
    public long RoleId;
    public Models.Notification Notification;
}

public class PushNotificationWorker : IHostedService, IDisposable
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<PushNotificationWorker> _logger;

    // The unbounded channel for queuing notification actions.
    private readonly Channel<PushNotificationAction> _actionChannel =
        ThreadChannel.CreateUnbounded<PushNotificationAction>();

    // Fields to manage the background processing task.
    private Task? _executingTask;
    private CancellationTokenSource? _cts;
    private bool _pushEnabled;

    public PushNotificationWorker(ILogger<PushNotificationWorker> logger,
                                  IServiceScopeFactory serviceScopeFactory, 
                                  IOptions<NotificationsConfig> notificationsConfig)
    {
        _logger = logger;
        _serviceScopeFactory = serviceScopeFactory;
    }

    /// <summary>
    /// Enqueue a push notification action.
    /// </summary>
    public ValueTask QueueNotificationAction(PushNotificationAction action)
        => _actionChannel.Writer.WriteAsync(action);

    /// <summary>
    /// Starts the background processing loop on a separate task.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _pushEnabled = NotificationsConfig.Current is not null &&
                       !string.IsNullOrWhiteSpace(NotificationsConfig.Current.PrivateKey);

        if (!_pushEnabled)
        {
            _logger.LogWarning("Notifications config missing - push sends will be disabled");
        }
        else
        {
            await ClearExpiredSubscriptions();
        }

        // Create a linked cancellation token and start the background loop.
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _executingTask = Task.Run(() => ProcessQueueAsync(_cts.Token), _cts.Token);
    }
    
    private async Task ClearExpiredSubscriptions()
    {
        using var scope = _serviceScopeFactory.CreateScope();
        var pushService = scope.ServiceProvider.GetRequiredService<PushNotificationService>();
        await pushService.ClearExpiredSubscriptionsAsync();
    }

    /// <summary>
    /// Continuously processes actions from the channel.
    /// </summary>
    private async Task ProcessQueueAsync(CancellationToken token)
    {
        try
        {
            while (await _actionChannel.Reader.WaitToReadAsync(token))
            {
                while (_actionChannel.Reader.TryRead(out var action))
                {
                    try
                    {
                        await HandleAction(action);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing push notification action: {ActionType}", action.GetType().Name);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when the token is canceled.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in ProcessQueueAsync.");
        }
    }

    /// <summary>
    /// Dispatches the action to the appropriate processing method.
    /// </summary>
    private async Task HandleAction(PushNotificationAction action)
    {
        switch (action)
        {
            case PushNotificationSubscribe subscribe:
                await ProcessSubscribe(subscribe.Subscription);
                break;
            case PushNotificationUnsubscribe unsubscribe:
                await ProcessUnsubscribe(unsubscribe.Subscription);
                break;
            case SendRolePushNotification roleMention:
                if (_pushEnabled)
                    await ProcessRoleNotification(roleMention.RoleId, roleMention.Content);
                break;
            case SendUserPushNotification userMention:
                if (_pushEnabled)
                    await ProcessUserNotification(userMention.UserId, userMention.Content);
                break;
            case SendMemberPushNotification memberMention:
                if (_pushEnabled)
                    await ProcessMemberNotification(memberMention.MemberId, memberMention.Content);
                break;
            case QueueRoleMentionNotification roleMentionNotification:
                await ProcessRoleMentionNotification(roleMentionNotification.RoleId, roleMentionNotification.Notification);
                break;
            default:
                _logger.LogWarning("Unknown notification action: {ActionType}", action.GetType().Name);
                break;
        }
    }

    private async Task ProcessSubscribe(PushNotificationSubscription subscription)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("Processing subscription for user {UserId}", subscription.UserId);
        using var scope = _serviceScopeFactory.CreateScope();
        var pushService = scope.ServiceProvider.GetRequiredService<PushNotificationService>();
        await pushService.SubscribeAsync(subscription);
        sw.Stop();
        _logger.LogInformation("Subscription processed in {ElapsedMilliseconds}ms", sw.ElapsedMilliseconds);
    }

    private async Task ProcessUnsubscribe(PushNotificationSubscription subscription)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("Processing unsubscription for user {UserId}", subscription.UserId);
        using var scope = _serviceScopeFactory.CreateScope();
        var pushService = scope.ServiceProvider.GetRequiredService<PushNotificationService>();
        await pushService.UnsubscribeAsync(subscription);
        sw.Stop();
        _logger.LogInformation("Unsubscription processed in {ElapsedMilliseconds}ms", sw.ElapsedMilliseconds);
    }

    private async Task ProcessRoleNotification(long roleId, NotificationContent content)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("Processing role mention for role {RoleId}", roleId);
        using var scope = _serviceScopeFactory.CreateScope();
        var pushService = scope.ServiceProvider.GetRequiredService<PushNotificationService>();
        await pushService.SendRolePushNotificationsAsync(roleId, content);
        sw.Stop();
        _logger.LogInformation("Role mention processed in {ElapsedMilliseconds}ms", sw.ElapsedMilliseconds);
    }

    private async Task ProcessUserNotification(long userId, NotificationContent content)
    {
        using var scope = _serviceScopeFactory.CreateScope();
        var pushService = scope.ServiceProvider.GetRequiredService<PushNotificationService>();
        await pushService.SendUserPushNotificationAsync(userId, content);
    }

    private async Task ProcessMemberNotification(long memberId, NotificationContent content)
    {
        using var scope = _serviceScopeFactory.CreateScope();
        var pushService = scope.ServiceProvider.GetRequiredService<PushNotificationService>();
        await pushService.SendMemberPushNotificationAsync(memberId, content);
    }

    private async Task ProcessRoleMentionNotification(long roleId, Models.Notification notification)
    {
        var sw = Stopwatch.StartNew();
        using var scope = _serviceScopeFactory.CreateScope();
        var notificationService = scope.ServiceProvider.GetRequiredService<NotificationService>();
        await notificationService.SendRoleNotificationsAsync(roleId, notification);
        sw.Stop();
        _logger.LogInformation("Role mention notification fanout processed in {ElapsedMilliseconds}ms", sw.ElapsedMilliseconds);
    }

    /// <summary>
    /// Signals cancellation and waits for the background task to finish.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Notifications Worker is stopping");
        if (_cts != null)
        {
            await _cts.CancelAsync();
        }

        if (_executingTask != null)
        {
            await Task.WhenAny(_executingTask, Task.Delay(Timeout.Infinite, cancellationToken));
        }
    }

    public void Dispose()
    {
        _cts?.Dispose();
    }
}
