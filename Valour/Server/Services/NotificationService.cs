using Valour.Server.Database;
using Valour.Server.Workers;
using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Server.Services;

public class NotificationService
{
    private readonly ValourDb _db;
    private readonly CoreHubService _coreHub;
    private readonly NodeLifecycleService _nodeLifecycleService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NotificationService> _logger;
    private readonly PlanetPermissionService _permissionService;
    private readonly PushNotificationWorker _pushNotificationWorker;
    
    public NotificationService(
        ValourDb db, 
        CoreHubService coreHub,
        NodeLifecycleService nodeLifecycleService,
        IServiceScopeFactory scopeFactory, 
        ILogger<NotificationService> logger, PlanetPermissionService permissionService, PushNotificationWorker pushNotificationWorker)
    {
        _db = db;
        _coreHub = coreHub;
        _nodeLifecycleService = nodeLifecycleService;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _permissionService = permissionService;
        _pushNotificationWorker = pushNotificationWorker;
    }
    
    public async Task<Models.Notification> GetNotificationAsync(long id)
        => (await _db.Notifications.FindAsync(id)).ToModel();

    public async Task<TaskResult> SetNotificationRead(long id, bool value)
    {
        var notification = await _db.Notifications.FindAsync(id);
        if (notification is null)
            return new TaskResult(false, "Notification not found");

        if (value)
        {
            notification.TimeRead = DateTime.UtcNow;
        }
        else
        {
            notification.TimeRead = null;
        }

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (Exception)
        {
            return new TaskResult(false, "Error saving changes to database");
        }

        _coreHub.RelayNotificationReadChange(notification.ToModel(), _nodeLifecycleService);
        
        return TaskResult.SuccessResult;
    }

    public async Task<TaskResult> ClearNotificationsForUser(long userId)
    {
        int changes = 0;
        
        try
        {
            changes = await _db.Notifications.Where(x => x.UserId == userId && x.TimeRead == null).ExecuteUpdateAsync(
                x => x.SetProperty(n => n.TimeRead, DateTime.UtcNow));
        }
        catch (Exception)
        {
            return new TaskResult(false, "Error saving changes to database");
        }
        
        _coreHub.RelayNotificationsCleared(userId, _nodeLifecycleService);

        return new TaskResult(true, $"Cleared {changes} notifications");
    }

    public async Task<List<Models.Notification>> GetNotifications(long userId, int page = 0)
        => await _db.Notifications.Where(x => x.UserId == userId)
            .OrderBy(x => x.TimeSent)
            .Skip(page * 50)
            .Take(50)
            .Select(x => x.ToModel())
            .ToListAsync();
    
    public async Task<List<Models.Notification>> GetUnreadNotifications(long userId, int page = 0)
        => await _db.Notifications.Where(x => x.UserId == userId && x.TimeRead == null)
            .OrderBy(x => x.TimeSent)
            .Skip(page * 50)
            .Take(50)
            .Select(x => x.ToModel())
            .ToListAsync();
    
    public async Task<List<Models.Notification>> GetAllUnreadNotifications(long userId)
        => await _db.Notifications.Where(x => x.UserId == userId && x.TimeRead == null)
            .OrderBy(x => x.TimeSent)
            .Select(x => x.ToModel())
            .ToListAsync();
    
    public async Task SendUserNotification(long userId, Models.Notification notification)
    {
        notification.UserId = userId;
        notification.TimeSent = DateTime.UtcNow;
        
        await _db.Notifications.AddAsync(notification.ToDatabase());
        await _db.SaveChangesAsync();
        
        _coreHub.RelayNotification(notification, _nodeLifecycleService);
        
        await _pushNotificationWorker.QueueNotificationAction(new SendUserPushNotification()
        {
            Content = new NotificationContent()
            {
                Title = notification.Title,
                Message = notification.Body,
                IconUrl = notification.ImageUrl,
                Url = notification.ClickUrl,
            },
            UserId = userId
        });
    }
    
    public async Task SendRoleNotificationsAsync(long roleId, Models.Notification baseNotification)
    {
        // Create db notifications
        
        // TODO: this could be a lot of data to move. may want a sequence or something on the db
        // at some point to handle this.
        var notifications = await _db.PlanetRoleMembers
            .Where(x => x.RoleId == roleId)
            .Select(x => new Valour.Database.Notification()
            {
                Id = IdManager.Generate(),
                Title = baseNotification.Title,
                Body = baseNotification.Body,
                ImageUrl = baseNotification.ImageUrl,
                ClickUrl = baseNotification.ClickUrl,
                PlanetId = baseNotification.PlanetId,
                ChannelId = baseNotification.ChannelId,
                Source = NotificationSource.PlanetRoleMention,
                SourceId = baseNotification.SourceId,
                UserId = x.UserId,
                TimeSent = DateTime.UtcNow,
            }).ToListAsync();
        
        await _db.Notifications.AddRangeAsync(notifications);
        await _db.SaveChangesAsync();
        
        // Send push notifications
        await _pushNotificationWorker.QueueNotificationAction(new SendRolePushNotification()
        {
            Content = new NotificationContent()
            {
                Title = baseNotification.Title,
                Message = baseNotification.Body,
                IconUrl = baseNotification.ImageUrl,
                Url = baseNotification.ClickUrl,
            },
            RoleId = roleId
        });
    }

    public Task HandleMentionAsync(
        Mention mention, 
        ISharedPlanet planet, 
        ISharedMessage message, 
        ISharedPlanetMember member, 
        ISharedUser user, 
        ISharedChannel channel)
    {
        switch (mention.Type)
        {
            case MentionType.PlanetMember:
                return HandleMemberMentionAsync(mention, planet, message, member, user, channel);
            case MentionType.User:
                return HandleUserMentionAsync(mention, message, user, channel);
            case MentionType.Role:
                return HandleRoleMentionAsync(mention, planet, message, member, user, channel);
            default:
                return Task.CompletedTask;
        }
    }
    
    private async Task HandleUserMentionAsync(
        Mention mention,
        ISharedMessage message,
        ISharedUser user,
        ISharedChannel channel
    )
    {
        // Ensure that the user is a member of the channel
        if (await _db.ChannelMembers.AnyAsync(x =>
            x.UserId == mention.TargetId && x.ChannelId == message.ChannelId))
            return;
        
        var mentionTargetUser = await _db.Users.FindAsync(mention.TargetId);

        var content = message.Content.Replace($"«@u-{mention.TargetId}»", $"@{mentionTargetUser.Name}");

        Models.Notification notification = new()
        {
            Title = user.Name + " mentioned you in DMs",
            Body = content,
            ImageUrl = ISharedUser.GetAvatar(user, AvatarFormat.Webp128),
            ClickUrl = $"/channels/direct/{channel.Id}/{message.Id}",
            ChannelId = channel.Id,
            Source = NotificationSource.DirectMention,
            SourceId = message.Id,
            UserId = mentionTargetUser.Id,
        };
        
        // Add notification to database
        await _db.Notifications.AddAsync(notification.ToDatabase());
        await _db.SaveChangesAsync();

        // Send notification to all of the user's online Valour instances
        _coreHub.RelayNotification(notification, _nodeLifecycleService);
        
        // Send push notification
        await _pushNotificationWorker.QueueNotificationAction(new SendUserPushNotification()
        {
            Content = new NotificationContent()
            {
                Title = notification.Title,
                Message = notification.Body,
                IconUrl = notification.ImageUrl,
                Url = notification.ClickUrl,
            },
            UserId = mentionTargetUser.Id
        });
    }

    private async Task HandleMemberMentionAsync(
        Mention mention,
        ISharedPlanet planet,
        ISharedMessage message,
        ISharedPlanetMember member,
        ISharedUser user,
        ISharedChannel channel
    )
    {
        // Member mentions only work in planet channels
        if (planet is null)
            return;
            
        var targetMember = await _db.PlanetMembers.FindAsync(mention.TargetId);
        if (targetMember is null)
            return;

        var content = message.Content.Replace($"«@m-{mention.TargetId}»", $"@{targetMember.Nickname}");

        Models.Notification notification = new()
        {
            Id = IdManager.Generate(),
            Title = member.Nickname + " in " + planet.Name,
            Body = content,
            ImageUrl = ISharedUser.GetAvatar(user, AvatarFormat.Webp128),
            UserId = targetMember.UserId,
            PlanetId = planet.Id,
            ChannelId = channel.Id,
            SourceId = message.Id,
            Source = NotificationSource.PlanetMemberMention,
            ClickUrl = $"planets/{planet.Id}/channels/{channel.Id}/{message.Id}",
            TimeSent = DateTime.UtcNow
        };
                
        // Add notification to database
        await _db.Notifications.AddAsync(notification.ToDatabase());
        await _db.SaveChangesAsync();

        // Send notification to all of the user's online Valour instances
        _coreHub.RelayNotification(notification, _nodeLifecycleService);
                
        // Send push notification
        await _pushNotificationWorker.QueueNotificationAction(new SendMemberPushNotification()
        {
            Content = new NotificationContent()
            {
                Title = notification.Title,
                Message = notification.Body,
                IconUrl = notification.ImageUrl,
                Url = notification.ClickUrl,
            },
            MemberId = targetMember.Id
        });
    }

    private async Task HandleRoleMentionAsync(
        Mention mention,
        ISharedPlanet planet,
        ISharedMessage message,
        ISharedPlanetMember member,
        ISharedUser user,
        ISharedChannel channel
    )
    {
        // Member mentions only work in planet channels
        if (planet is null)
            return;
            
        var targetRole = await _db.PlanetRoles.FindAsync(mention.TargetId);
        if (targetRole is null)
            return;
	        
        var content = message.Content.Replace($"«@r-{mention.TargetId}»", $"@{targetRole.Name}");

        Models.Notification notif = new()
        {
            Title = member.Nickname + " in " + planet.Name,
            Body = content,
            ImageUrl = ISharedUser.GetAvatar(user, AvatarFormat.Webp128),
            PlanetId = planet.Id,
            ChannelId = channel.Id,
            SourceId = message.Id,
            ClickUrl = $"planets/{planet.Id}/channels/{channel.Id}/{message.Id}"
        };

        // Add notification to database
        await _db.Notifications.AddAsync(notif.ToDatabase());
        await _db.SaveChangesAsync();
        
        // Send notification to all of the user's online Valour instances
        _coreHub.RelayNotification(notif, _nodeLifecycleService);
        
        // Send push notification
        await _pushNotificationWorker.QueueNotificationAction(new SendRolePushNotification()
        {
            Content = new NotificationContent()
            {
                Title = notif.Title,
                Message = notif.Body,
                IconUrl = notif.ImageUrl,
                Url = notif.ClickUrl,
            },
            RoleId = targetRole.Id,
        });
    }
}