using System.Collections.Concurrent;
using System.Net;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using Valour.Config.Configs;
using Valour.Server.Database;
using Valour.Shared.Models;
using WebPush;

namespace Valour.Server.Services;

public class PushNotificationService
{
    private readonly ValourDb _db;
    private readonly ILogger<PushNotificationService> _logger;
    private readonly PlanetPermissionService _permissionService;
    private readonly WebPushClient _webPushClient;
    private readonly VapidDetails _vapidDetails;
    
    public PushNotificationService(
        ILogger<PushNotificationService> logger, 
        ValourDb db, 
        PlanetPermissionService permissionService)
    {
        _logger = logger;
        _db = db;
        _permissionService = permissionService;
        _webPushClient = new WebPushClient();
        
        _vapidDetails = new VapidDetails()
        {
            Subject = NotificationsConfig.Current?.Subject,
            PublicKey = NotificationsConfig.Current?.PublicKey,
            PrivateKey = NotificationsConfig.Current?.PrivateKey
        };
    }
    
    public async Task ClearExpiredSubscriptionsAsync()
    {
        var expiredSubs = await _db.PushNotificationSubscriptions
            .Where(x => x.ExpiresAt < DateTime.UtcNow)
            .ExecuteDeleteAsync();
        
        _logger.LogInformation("Cleared {Count} expired subscriptions", expiredSubs);
    }
    
    public async Task SubscribeAsync(PushNotificationSubscription subscription)
    {
        // Check if subscription already exists in db
        var existingUserSub = 
            await _db.PushNotificationSubscriptions.FirstOrDefaultAsync(x => 
                x.Endpoint == subscription.Endpoint);

        if (existingUserSub is not null)
        {
            // Update existing subscription
            existingUserSub.Auth = subscription.Auth;
            existingUserSub.Key = subscription.Key;
            
            _db.PushNotificationSubscriptions.Update(existingUserSub);
        }
        else
        {
            var newUserSub = subscription.ToDatabase();
            newUserSub.Id = IdManager.Generate();
            
            // Add new subscription
            await _db.PushNotificationSubscriptions.AddAsync(newUserSub);
        }
        
        await _db.SaveChangesAsync();
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task UnsubscribeAsync(ISharedPushNotificationSubscription subscription)
        => UnsubscribeAsync(subscription.Endpoint);
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async Task UnsubscribeAsync(string endpoint)
    {
        // Get all subscriptions for this endpoint
        var subscriptions = await _db.PushNotificationSubscriptions
            .Where(x => x.Endpoint == endpoint)
            .ExecuteDeleteAsync();
        
        _logger.LogInformation("Deleted {Count} subscriptions for endpoint {Endpoint}", subscriptions, endpoint);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private string GetPayload(NotificationContent content)
    {
        return 
        $@"{{
            ""title"": ""{content.Title}"",
            ""message"": ""{content.Message}"",
            ""iconUrl"": ""{content.IconUrl}"",
            ""url"": ""{content.Url}""
        }}";
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async Task SendNotificationAsync(
        ISharedPushNotificationSubscription sub, 
        string payload, 
        ConcurrentBag<string> unsubscribes, 
        CancellationToken cancellationToken
    )
    {
        var webSub = new PushSubscription(sub.Endpoint, sub.Key, sub.Auth);
        try
        {
            await _webPushClient.SendNotificationAsync(webSub, payload, _vapidDetails,
                cancellationToken: cancellationToken);
        }
        catch (WebPushException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Gone)
            {
                _logger.LogInformation("Subscription {Endpoint} is no longer valid", sub.Endpoint);
                unsubscribes.Add(sub.Endpoint);
            }
            else
            {
                _logger.LogError(ex, "Failed to send notification to {Endpoint}", sub.Endpoint);
            }
        }
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async Task SendParallelNotificationsAsync(
        Valour.Database.PushNotificationSubscription[] subs, 
        string payload
    )
    {
        var unsubscribes = new ConcurrentBag<string>();
        
        await Parallel.ForEachAsync(subs, async (sub, cancellationToken) =>
        {
            await SendNotificationAsync(sub, payload, unsubscribes, cancellationToken);
        });
        
        // Remove invalid subscriptions
        foreach (var endpoint in unsubscribes)
        {
            await UnsubscribeAsync(endpoint);
        }
    }
    
    /// <summary>
    /// Sends a notification to the given user
    /// </summary>
    public async Task SendUserPushNotificationAsync(long userId, NotificationContent content)
    {
        // Get user's push subscriptions
        var subs = await _db.PushNotificationSubscriptions
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .ToArrayAsync();
        
        var payload = GetPayload(content);
        
        await SendParallelNotificationsAsync(subs, payload);
    }
    
    /// <summary>
    /// Sends a notification to the given member
    /// </summary>
    public async Task SendMemberPushNotificationAsync(long memberId, NotificationContent content)
    {
        // Get subscriptions
        var subscriptions = await _db.PlanetMembers
            .AsNoTracking()
            .Where(x => x.Id == memberId)
            .Select(x => x.User.NotificationSubscriptions)
            // Flatten
            .SelectMany(x => x)
            .ToArrayAsync();
        
        var payload = GetPayload(content);
        
        await SendParallelNotificationsAsync(subscriptions, payload);
    }
    
    /// <summary>
    /// Sends a notification to all users in a role
    /// </summary>
    public async Task SendRolePushNotificationsAsync(long roleId, NotificationContent content)
    {
        // Now get all the subscriptions for users with these role combos
        var subscriptions = await _db.PlanetRoleMembers
            .AsNoTracking()
            .Where(x => x.RoleId == roleId)
            .Select(x => x.User.NotificationSubscriptions)
            // Flatten
            .SelectMany(x => x)
            .ToArrayAsync();
        
        var payload = GetPayload(content);
        
        await SendParallelNotificationsAsync(subscriptions, payload);
        
        _logger.LogInformation("Sent role mention for {SubscriptionCount} subscriptions", subscriptions.Length);
    }
}