using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Azure.NotificationHubs;
using Valour.Config.Configs;

using ThreadChannel = System.Threading.Channels.Channel;

namespace Valour.Server.Workers;

public class PushNotificationAction {}

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
    
    /// <summary>
    /// Queue for role hash changes that need to be made for member notifications
    /// </summary>
    private Channel<PushNotificationAction> _actionChannel = 
        ThreadChannel.CreateUnbounded<PushNotificationAction>();
    
    public PushNotificationWorker(ILogger<PushNotificationWorker> logger,
        IServiceScopeFactory serviceScopeFactory)
    {
        _logger = logger;
        _serviceScopeFactory = serviceScopeFactory;
        
        if (!string.IsNullOrWhiteSpace(NotificationsConfig.Current.AzureConnectionString))
        {
            _hubClient = NotificationHubClient.CreateClientFromConnectionString(
                NotificationsConfig.Current.AzureConnectionString, 
                NotificationsConfig.Current.AzureHubName);
        }
    }
    
    public ValueTask QueueNotificationAction(PushNotificationAction action)
        => _actionChannel.Writer.WriteAsync(action);
    
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_hubClient is null)
        {
            _logger.LogWarning("Notifications config missing - disabling worker");
            return;
        }

        while (await _actionChannel.Reader.WaitToReadAsync(cancellationToken))
        {
            while (_actionChannel.Reader.TryRead(out var action))
            {
                await HandleAction(action);
            }
        }
    }
    
    private Task HandleAction(PushNotificationAction action)
    {
        switch (action)
        {
            case PushNotificationRoleHashChange roleHashChange:
                return ProcessRoleHashChange(roleHashChange.OldHash, roleHashChange.NewHash);
            case PushNotificationSubscribe subscribe:
                return ProcessSubscribe(subscribe.Subscription);
            case PushNotificationUnsubscribe unsubscribe:
                return ProcessUnsubscribe(unsubscribe.Subscription);
            case SendRolePushNotification roleMention:
                return ProcessRoleNotification(roleMention.RoleId, roleMention.Content);
            case SendUserPushNotification userMention:
                return ProcessUserNotification(userMention.UserId, userMention.Content);
            case SendMemberPushNotification memberMention:
                return ProcessMemberNotification(memberMention.MemberId, memberMention.Content);
            default:
                _logger.LogWarning("Unknown notification action: {ActionType}", action.GetType().Name);
                break;
        }
        
        return Task.CompletedTask;
    }

    private Stopwatch _sw = new();
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
    
    public Task StopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Notifications Worker is Stopping");
        return Task.CompletedTask;
    }

    public void Dispose()
    {
    }
}