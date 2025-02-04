using System.Security.Cryptography;
using System.Text;
using Microsoft.Azure.NotificationHubs;
using Valour.Config.Configs;
using Valour.Shared.Models;

namespace Valour.Server.Services;

public class PushNotificationService
{
    private readonly ValourDb _db;
    private readonly NotificationHubClient _hubClient;
    private readonly ILogger<PushNotificationService> _logger;
    private readonly SHA256 _sha256 = SHA256.Create();
    private readonly PlanetPermissionService _permissionService;
    
    public PushNotificationService(
        ILogger<PushNotificationService> logger, 
        ValourDb db, PlanetPermissionService 
            permissionService)
    {
        _logger = logger;
        _db = db;
        _permissionService = permissionService;

        if (!string.IsNullOrWhiteSpace(NotificationsConfig.Current.AzureConnectionString))
        {
            _hubClient = NotificationHubClient.CreateClientFromConnectionString(
                NotificationsConfig.Current.AzureConnectionString, 
                NotificationsConfig.Current.AzureHubName);
        }
    }
    
    private string GetSubscriptionId(string endpoint, long? planetId)
    {
        var baseId = endpoint;
        if (planetId is not null)
            baseId += planetId.ToString();
        
        var subIdBytes = _sha256.ComputeHash(Encoding.UTF8.GetBytes(baseId));
        return Convert.ToBase64String(subIdBytes);
    }
    
    public List<string> GetTagsForMember(ISharedPlanetMember member) =>
        GetPlanetTags(member.PlanetId, member.RoleHashKey, member.Id);

    public List<string> GetPlanetTags(long planetId, long roleHashKey, long memberId)
    {
        return new List<string> { $"p:{planetId}", $"rh:{roleHashKey}", $"m:{memberId}" }; // planet, role hash, member
    }
    
    public async Task ReplaceRoleHashTags(long oldHashKey, long newHashKey)
    {
        // Get all subscriptions with the old hash key
        var subscriptions = await _db.PushNotificationSubscriptions
            .Where(x => x.RoleHashKey == oldHashKey)
            .ToListAsync();
        
        _logger.LogInformation("Replacing role hash key {OldHashKey} with {NewHashKey} in {Count} subscriptions", oldHashKey, newHashKey, subscriptions.Count);

        foreach (var sub in subscriptions)
        {
            if (sub.PlanetId is null || sub.MemberId is null)
            {
                // Malformed subscription, remove it
                _db.PushNotificationSubscriptions.Remove(sub);
                continue;
            }
        
            // Update in database
            sub.RoleHashKey = newHashKey;
        
            // Need to update tag in azure
            var newTags = GetPlanetTags(sub.PlanetId.Value, sub.RoleHashKey.Value, sub.MemberId.Value);
        
            var subId = GetSubscriptionId(sub.Endpoint, sub.PlanetId);
            
            await _hubClient.PatchInstallationAsync(subId, new List<PartialUpdateOperation>(){
                new ()
                {
                    Operation = UpdateOperationType.Replace,
                    Path = "/tags",
                    Value = string.Join(',', newTags)
                }
            });
        }
    }
    
    public async Task SubscribeAsync(PushNotificationSubscription subscription)
    {
        await SubscribeUserAsync(subscription);
        await SubscribePlanetsAsync(subscription);
    }

    /// <summary>
    /// Sets up the base user notifications
    /// </summary>
    public async Task SubscribeUserAsync(PushNotificationSubscription subscription)
    {
        var tags = new List<string> { $"u:{subscription.UserId}" }; // user
        
        var registration = await _hubClient.CreateBrowserNativeRegistrationAsync(
            subscription.Endpoint, 
            subscription.Auth, 
            subscription.Key, 
            tags,
            DateTime.UtcNow.AddDays(7)
        );
        
        
        /*
        
        var userInstallation = CreateInstallation(subscription.Endpoint, subscription.Key, subscription.Auth, null, tags);
        
        var subId = GetSubscriptionId(endpoint, planetId);
        x.RegistrationId = subId;
        x.ExpirationTime = DateTime.UtcNow.AddDays(7);
        
        new Installation
        {
            InstallationId = subId,
            PushChannel = endpoint,
            ExpirationTime = DateTime.UtcNow.AddDays(7),
            Tags = tags,
        }
        
        
       
       
       _hubClient.regist
        
        
        // Check if subscription already exists in db
        var existingUserSub = 
            await _db.PushNotificationSubscriptions.FirstOrDefaultAsync(x => 
                x.Endpoint == subscription.Endpoint &&
                x.PlanetId == null);

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
            
            // Add new subscription
            await _db.PushNotificationSubscriptions.AddAsync(newUserSub);
        }

        // Create or update on azure
        await _hubClient.CreateOrUpdateInstallationAsync(userInstallation);
        
        await _db.SaveChangesAsync();
        */
    }

    public async Task SubscribePlanetsAsync(PushNotificationSubscription userSubscription)
    {
        /*
        
        // Get PlanetId and RoleHashKey from all planetmembers for user
        var membership = await _db.PlanetMembers
            .Where(x => x.UserId == userSubscription.UserId)
            .ToListAsync();
        
        // Create installation for each planet

        foreach (var member in membership)
        {
            var tags = GetTagsForMember(member); // planet, role hash, member
            var planetInstallation = CreateInstallation(userSubscription.Endpoint, userSubscription.Key, userSubscription.Auth, member.PlanetId, tags);
            
            // Check if subscription already exists in db
            var existingPlanetSub = 
                await _db.PushNotificationSubscriptions.FirstOrDefaultAsync(x => 
                    x.Endpoint == userSubscription.Endpoint &&
                    x.PlanetId == member.PlanetId);

            if (existingPlanetSub is not null)
            {
                existingPlanetSub.Auth = userSubscription.Auth;
                existingPlanetSub.Key = userSubscription.Key;
                
                _db.PushNotificationSubscriptions.Update(existingPlanetSub);
            } 
            else
            {
                var newPlanetSub = userSubscription.ToDatabase();
                newPlanetSub.PlanetId = member.PlanetId;
                newPlanetSub.RoleHashKey = member.RoleHashKey;
                newPlanetSub.MemberId = member.Id;
                
                await _db.PushNotificationSubscriptions.AddAsync(newPlanetSub);
            }

            // Create or update on azure
            await _hubClient.CreateOrUpdateInstallationAsync(planetInstallation);
        }
        
        await _db.SaveChangesAsync();
        
        */
    }
    
    public async Task UnsubscribeAsync(PushNotificationSubscription subscription)
    {
        // Get all subscriptions for this endpoint
        var subscriptions = await _db.PushNotificationSubscriptions
            .Where(x => x.Endpoint == subscription.Endpoint)
            .ToListAsync();

        foreach (var sub in subscriptions)
        {
            // Remove from azure
            var subId = GetSubscriptionId(subscription.Endpoint, subscription.PlanetId);
            await _hubClient.DeleteInstallationAsync(subId);
            
            // Remove from db
            _db.PushNotificationSubscriptions.Remove(sub);
        }
        
        await _db.SaveChangesAsync();
    }
    
    /// <summary>
    /// Updates planet subscriptions for a member whose roles have changed
    /// </summary>
    public async Task HandleMemberRolesChanged(ISharedPlanetMember member)
    {
        var newTags = GetTagsForMember(member);
        
        // Get all subscriptions for this member
        var subscriptions = await _db.PushNotificationSubscriptions
            .Where(x => x.UserId == member.UserId && x.PlanetId == member.PlanetId)
            .Select(x => new { x.Endpoint, x.PlanetId })
            .ToListAsync();

        foreach (var sub in subscriptions)
        {
            var subId = GetSubscriptionId(sub.Endpoint, sub.PlanetId);
            
            await _hubClient.PatchInstallationAsync(subId, new List<PartialUpdateOperation>(){
                new ()
                {
                    Operation = UpdateOperationType.Replace,
                    Path = "/tags",
                    Value = string.Join(',', newTags)
                }
            });
        }
    }
    
    /// <summary>
    /// Sends a notification to the given user
    /// </summary>
    public async Task SendUserPushNotificationAsync(long userId, NotificationContent content)
    {
        await _hubClient.SendNotificationAsync(new TemplateNotification(
            new Dictionary<string, string>()
            {
                ["title"] = content.Title,
                ["message"] = content.Message,
                ["iconUrl"] = content.IconUrl,
                ["url"] = content.Url,
            }
        ), $"u:{userId}");
    }
    
    /// <summary>
    /// Sends a notification to the given member
    /// </summary>
    public async Task SendMemberPushNotificationAsync(long memberId, NotificationContent content)
    {
        await _hubClient.SendNotificationAsync(new TemplateNotification(
            new Dictionary<string, string>()
            {
                ["title"] = content.Title,
                ["message"] = content.Message,
                ["iconUrl"] = content.IconUrl,
                ["url"] = content.Url,
            }
        ), $"m:{memberId}");
    }
    
    /// <summary>
    /// Sends a notification to all users in a role
    /// </summary>
    public async Task SendRolePushNotificationsAsync(long roleId, NotificationContent content)
    {
        // We need to get all the role combinations containing this role
        var roleCombos = await _permissionService.GetPlanetRoleComboKeysForRole(roleId);
        
        // Determine which of these combos actually have access to the channel
        // _permissionService.HasChannelAccessAsync()
        
        // NOTE: We don't actually have to do that, because we won't allow pinging a role that doesn't have access to the channel
        // anyways. So we can just send the notification to all members of the role.
        
        List<string> tags = new();
        foreach (var role in roleCombos)
        {
            tags.Add($"rh:{role}");
        }
        
        // With azure we can send up to 20 tags at once
        // So we need to split the tags into groups of 20
        foreach (var group in tags.Chunk(20))
        {
            var outcome = await _hubClient.SendNotificationAsync(new TemplateNotification(
                new Dictionary<string, string>()
                {
                    ["title"] = content.Title,
                    ["message"] = content.Message,
                    ["iconUrl"] = content.IconUrl,
                    ["url"] = content.Url,
                }
            ), group);

            _logger.LogInformation("Sent role mention: {SuccessCount} succeeded, {FailureCount} failed", outcome.Success, outcome.Failure);
        }
    }
}