using Microsoft.Extensions.Options;
using Valour.Config.Configs;
using Valour.Server.Database;
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
    
    public async Task UnsubscribeAsync(PushNotificationSubscription subscription)
    {
        // Get all subscriptions for this endpoint
        var subscriptions = await _db.PushNotificationSubscriptions
            .Where(x => x.Endpoint == subscription.Endpoint)
            .ExecuteDeleteAsync();
        
        _logger.LogInformation("Deleted {Count} subscriptions for endpoint {Endpoint}", subscriptions, subscription.Endpoint);
    }
    
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
        
        await Parallel.ForEachAsync(subs, async (sub, cancellationToken) =>
        {
            var webSub = new PushSubscription(sub.Endpoint, sub.Key, sub.Auth);
            await _webPushClient.SendNotificationAsync(webSub, payload, _vapidDetails, cancellationToken: cancellationToken);
        });
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
        
        await Parallel.ForEachAsync(subscriptions, async (sub, cancellationToken) =>
        {
            var webSub = new PushSubscription(sub.Endpoint, sub.Key, sub.Auth);
            await _webPushClient.SendNotificationAsync(webSub, payload, _vapidDetails, cancellationToken: cancellationToken);
        });
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
        
        await Parallel.ForEachAsync(subscriptions, async (sub, cancellationToken) =>
        {
            var webSub = new PushSubscription(sub.Endpoint, sub.Key, sub.Auth);
            await _webPushClient.SendNotificationAsync(webSub, payload, _vapidDetails, cancellationToken: cancellationToken);
        });
        
        _logger.LogInformation("Sent role mention for {SubscriptionCount} subscriptions", subscriptions.Length);
    }
}