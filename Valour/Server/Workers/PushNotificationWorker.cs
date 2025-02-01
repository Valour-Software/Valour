using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Azure.NotificationHubs;
using Valour.Config.Configs;
using ThreadChannel = System.Threading.Channels.Channel;

namespace Valour.Server.Workers;

public class PushNotificationAction { }

public class PushNotificationRoleHashChange : PushNotificationAction
{
    public long OldHash;
    public long NewHash;
}

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

public class PushNotificationWorker : IHostedService, IDisposable
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<PushNotificationWorker> _logger;
    private readonly NotificationHubClient _hubClient;

    // The unbounded channel for queuing notification actions.
    private Channel<PushNotificationAction> _actionChannel =
        ThreadChannel.CreateUnbounded<PushNotificationAction>();

    // Fields to manage the background processing task.
    private Task? _executingTask;
    private CancellationTokenSource? _cts;

    public PushNotificationWorker(ILogger<PushNotificationWorker> logger,
                                  IServiceScopeFactory serviceScopeFactory)
    {
        _logger = logger;
        _serviceScopeFactory = serviceScopeFactory;
        
        // Initialize the hub client if the configuration is present.
        if (!string.IsNullOrWhiteSpace(NotificationsConfig.Current.AzureConnectionString))
        {
            _hubClient = NotificationHubClient.CreateClientFromConnectionString(
                NotificationsConfig.Current.AzureConnectionString,
                NotificationsConfig.Current.AzureHubName);
        }
    }

    /// <summary>
    /// Enqueue a push notification action.
    /// </summary>
    public ValueTask QueueNotificationAction(PushNotificationAction action)
        => _actionChannel.Writer.WriteAsync(action);

    /// <summary>
    /// Starts the background processing loop on a separate task.
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // If no hub client is available, log a warning and do nothing.
        if (_hubClient is null)
        {
            _logger.LogWarning("Notifications config missing - disabling worker");
            return Task.CompletedTask;
        }

        // Create a linked cancellation token and start the background loop.
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _executingTask = Task.Run(() => ProcessQueueAsync(_cts.Token), _cts.Token);

        // Return immediately so that the host startup isn't delayed.
        return Task.CompletedTask;
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
            case PushNotificationRoleHashChange roleHashChange:
                await ProcessRoleHashChange(roleHashChange.OldHash, roleHashChange.NewHash);
                break;
            case PushNotificationSubscribe subscribe:
                await ProcessSubscribe(subscribe.Subscription);
                break;
            case PushNotificationUnsubscribe unsubscribe:
                await ProcessUnsubscribe(unsubscribe.Subscription);
                break;
            case SendRolePushNotification roleMention:
                await ProcessRoleNotification(roleMention.RoleId, roleMention.Content);
                break;
            case SendUserPushNotification userMention:
                await ProcessUserNotification(userMention.UserId, userMention.Content);
                break;
            case SendMemberPushNotification memberMention:
                await ProcessMemberNotification(memberMention.MemberId, memberMention.Content);
                break;
            default:
                _logger.LogWarning("Unknown notification action: {ActionType}", action.GetType().Name);
                break;
        }
    }

    private readonly Stopwatch _sw = new();
    
    private async Task ProcessRoleHashChange(long oldHashKey, long newHashKey)
    {
        _sw.Restart();
        _logger.LogInformation("Processing role hash change from {OldHashKey} to {NewHashKey}", oldHashKey, newHashKey);
        using var scope = _serviceScopeFactory.CreateScope();
        var pushService = scope.ServiceProvider.GetRequiredService<PushNotificationService>();
        await pushService.ReplaceRoleHashTags(oldHashKey, newHashKey);
        _sw.Stop();
        _logger.LogInformation("Role hash change processed in {ElapsedMilliseconds}ms", _sw.ElapsedMilliseconds);
    }

    private async Task ProcessSubscribe(PushNotificationSubscription subscription)
    {
        _sw.Restart();
        _logger.LogInformation("Processing subscription for user {UserId}", subscription.UserId);
        using var scope = _serviceScopeFactory.CreateScope();
        var pushService = scope.ServiceProvider.GetRequiredService<PushNotificationService>();
        await pushService.SubscribeAsync(subscription);
        _sw.Stop();
        _logger.LogInformation("Subscription processed in {ElapsedMilliseconds}ms", _sw.ElapsedMilliseconds);
    }

    private async Task ProcessUnsubscribe(PushNotificationSubscription subscription)
    {
        _sw.Restart();
        _logger.LogInformation("Processing unsubscription for user {UserId}", subscription.UserId);
        using var scope = _serviceScopeFactory.CreateScope();
        var pushService = scope.ServiceProvider.GetRequiredService<PushNotificationService>();
        await pushService.UnsubscribeAsync(subscription);
        _sw.Stop();
        _logger.LogInformation("Unsubscription processed in {ElapsedMilliseconds}ms", _sw.ElapsedMilliseconds);
    }

    private async Task ProcessRoleNotification(long roleId, NotificationContent content)
    {
        _sw.Restart();
        _logger.LogInformation("Processing role mention for role {RoleId}", roleId);
        using var scope = _serviceScopeFactory.CreateScope();
        var pushService = scope.ServiceProvider.GetRequiredService<PushNotificationService>();
        await pushService.SendRolePushNotificationsAsync(roleId, content);
        _sw.Stop();
        _logger.LogInformation("Role mention processed in {ElapsedMilliseconds}ms", _sw.ElapsedMilliseconds);
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
