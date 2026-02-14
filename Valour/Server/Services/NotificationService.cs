#nullable  enable

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
    private readonly ILogger<NotificationService> _logger;
    private readonly PushNotificationWorker _pushNotificationWorker;
    private readonly HostedPlanetService _hostedService;
    
    public NotificationService(
        ValourDb db, 
        CoreHubService coreHub,
        NodeLifecycleService nodeLifecycleService,
        ILogger<NotificationService> logger, 
        PushNotificationWorker pushNotificationWorker, 
        HostedPlanetService hostedService)
    {
        _db = db;
        _coreHub = coreHub;
        _nodeLifecycleService = nodeLifecycleService;
        _logger = logger;
        _pushNotificationWorker = pushNotificationWorker;
        _hostedService = hostedService;
    }
    
    public async Task<Models.Notification> GetNotificationAsync(Guid id)
        => (await _db.Notifications.FindAsync(id)).ToModel();

    public async Task<TaskResult> SetNotificationRead(Guid id, bool value)
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
        
        if (!await IsNotificationSourceEnabledForUserAsync(userId, notification.Source))
            return;

        notification.Id = Guid.NewGuid();

        notification.Body ??= "";
        
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
        var hostedPlanet = await _hostedService.GetRequiredAsync(baseNotification.PlanetId!.Value);
        var role = hostedPlanet.GetRoleById(roleId);
        if (role is null)
            return;

        var notificationSource = NotificationPreferences.IsSingleSource(baseNotification.Source)
            ? baseNotification.Source
            : NotificationSource.PlanetRoleMention;

        baseNotification.Body ??= "";

        var userIds = await _db.PlanetMembers
            .AsNoTracking()
            .WithRoleByLocalIndex(hostedPlanet.Planet.Id,  role.FlagBitIndex)
            .Select(x => x.UserId)
            .Distinct()
            .ToArrayAsync();

        if (userIds.Length == 0)
            return;

        var preferenceMasks = await _db.UserPreferences
            .AsNoTracking()
            .Where(x => userIds.Contains(x.Id))
            .Select(x => new
            {
                x.Id,
                x.EnabledNotificationSources
            })
            .ToDictionaryAsync(x => x.Id, x => x.EnabledNotificationSources);

        var filteredUserIds = userIds
            .Where(userId =>
            {
                if (!preferenceMasks.TryGetValue(userId, out var enabledSourcesMask))
                    return true;

                return NotificationPreferences.IsSourceEnabled(
                    enabledSourcesMask,
                    notificationSource
                );
            })
            .ToArray();

        if (filteredUserIds.Length == 0)
            return;

        var pushContent = new NotificationContent()
        {
            Title = baseNotification.Title,
            Message = baseNotification.Body,
            IconUrl = baseNotification.ImageUrl,
            Url = baseNotification.ClickUrl,
        };

        const int insertBatchSize = 2_000;
        var originalAutoDetect = _db.ChangeTracker.AutoDetectChangesEnabled;
        _db.ChangeTracker.AutoDetectChangesEnabled = false;
        var notificationsToRelay = new List<Models.Notification>(filteredUserIds.Length);

        try
        {
            foreach (var userBatch in filteredUserIds.Chunk(insertBatchSize))
            {
                var timeSent = DateTime.UtcNow;
                var notifications = userBatch.Select(userId => new Valour.Database.Notification()
                {
                    Id = Guid.NewGuid(),
                    Title = baseNotification.Title,
                    Body = baseNotification.Body,
                    ImageUrl = baseNotification.ImageUrl,
                    ClickUrl = baseNotification.ClickUrl,
                    PlanetId = baseNotification.PlanetId,
                    ChannelId = baseNotification.ChannelId,
                    Source = notificationSource,
                    SourceId = baseNotification.SourceId,
                    UserId = userId,
                    TimeSent = timeSent,
                }).ToArray();

                await _db.Notifications.AddRangeAsync(notifications);
                notificationsToRelay.AddRange(notifications.Select(x => x.ToModel()));
            }

            await _db.SaveChangesAsync();
        }
        finally
        {
            _db.ChangeTracker.AutoDetectChangesEnabled = originalAutoDetect;
            _db.ChangeTracker.Clear();
        }

        foreach (var notification in notificationsToRelay)
        {
            _coreHub.RelayNotification(notification, _nodeLifecycleService);
        }

        await _pushNotificationWorker.QueueNotificationAction(new SendUsersPushNotification()
        {
            UserIds = filteredUserIds,
            Content = pushContent
        });

        _logger.LogInformation(
            "Queued role mention notifications for {RecipientCount} users on role {RoleId}",
            filteredUserIds.Length,
            roleId
        );
    }

    public async Task HandleReplyAsync(
        ISharedMessage repliedToMessage,
        ISharedPlanet? planet,
        ISharedMessage message,
        ISharedPlanetMember? member,
        ISharedUser user,
        ISharedChannel channel)
    {
        Models.Notification notification = new()
        {
            Title = (member is null ? user.Name : member.Name) + " in " + channel.Name + (planet is null ? "" : $" ({planet.Name})"),
            Body = message.Content,
            ImageUrl = member is null ? user.GetAvatar() : member.GetAvatar(),
            ClickUrl = planet is null ? 
                $"channels/direct/{channel.Id}/{message.Id}" : 
                $"planets/{planet.Id}/channels/{channel.Id}/{message.Id}",
            ChannelId = channel.Id,
            Source = planet is null ? NotificationSource.DirectReply : NotificationSource.PlanetMemberReply,
            SourceId = message.Id,
            UserId = repliedToMessage.AuthorUserId,
        };

        await SendUserNotification(repliedToMessage.AuthorUserId, notification);
    }
    
    public async Task HandleDirectMessageAsync(
        ISharedMessage message,
        ISharedUser user,
        ISharedChannel channel)
    {
        // Get all members of the DM channel except the sender
        var recipientIds = await _db.ChannelMembers
            .Where(x => x.ChannelId == channel.Id && x.UserId != user.Id)
            .Select(x => x.UserId)
            .ToListAsync();

        foreach (var recipientId in recipientIds)
        {
            Models.Notification notification = new()
            {
                Title = user.Name + " DMed you.",
                Body = message.Content,
                ImageUrl = user.GetAvatar(),
                ClickUrl = $"channels/direct/{channel.Id}/{message.Id}",
                ChannelId = channel.Id,
                Source = NotificationSource.DirectMessage,
                SourceId = message.Id,
                UserId = recipientId,
            };

            await SendUserNotification(recipientId, notification);
        }
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
        if (!await _db.ChannelMembers.AnyAsync(x =>
            x.UserId == mention.TargetId && x.ChannelId == message.ChannelId))
            return;
        
        var mentionTargetUser = await _db.Users.FindAsync(mention.TargetId);

        var content = message.Content.Replace($"«@u-{mention.TargetId}»", $"@{mentionTargetUser.Name}");

        Models.Notification notification = new()
        {
            Title = user.Name + " mentioned you in DMs",
            Body = content,
            ImageUrl = ISharedUser.GetAvatar(user, AvatarFormat.Webp128),
            ClickUrl = $"channels/direct/{channel.Id}/{message.Id}",
            ChannelId = channel.Id,
            Source = NotificationSource.DirectMention,
            SourceId = message.Id,
            UserId = mentionTargetUser.Id,
        };
        
        await SendUserNotification(mentionTargetUser.Id, notification);
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

        var title = user.Id == ISharedUser.VictorUserId
            ? "Victor in " + planet.Name
            : member.Nickname + " in " + planet.Name;
        
        Models.Notification notification = new()
        {
            Id = Guid.NewGuid(),
            Title = title,
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

        await SendUserNotification(targetMember.UserId, notification);
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
        var mentionSource = GetRoleMentionSource(targetRole);

        var baseNotification = new Notification()
        {
            Id = Guid.NewGuid(),
            Title = member.Name + " in " + planet.Name,
            Body = content,
            ImageUrl = ISharedUser.GetAvatar(user, AvatarFormat.Webp128),
            PlanetId = planet.Id,
            ChannelId = channel.Id,
            Source = mentionSource,
            SourceId = message.Id,
            ClickUrl = $"planets/{planet.Id}/channels/{channel.Id}/{message.Id}"
        };

        await _pushNotificationWorker.QueueNotificationAction(new QueueRoleMentionNotification()
        {
            RoleId = mention.TargetId,
            Notification = baseNotification
        });
    }

    private static NotificationSource GetRoleMentionSource(ISharedPlanetRole role)
    {
        if (role.IsDefault || string.Equals(role.Name, "everyone", StringComparison.OrdinalIgnoreCase))
            return NotificationSource.PlanetEveryoneMention;

        if (string.Equals(role.Name, "here", StringComparison.OrdinalIgnoreCase))
            return NotificationSource.PlanetHereMention;

        return NotificationSource.PlanetRoleMention;
    }

    private async Task<bool> IsNotificationSourceEnabledForUserAsync(long userId, NotificationSource source)
    {
        if (!NotificationPreferences.IsSingleSource(source))
            return true;

        var enabledMask = await _db.UserPreferences
            .AsNoTracking()
            .Where(x => x.Id == userId)
            .Select(x => (long?)x.EnabledNotificationSources)
            .FirstOrDefaultAsync();

        if (enabledMask is null)
            return true;

        return NotificationPreferences.IsSourceEnabled(enabledMask.Value, source);
    }
}
