#nullable enable annotations

using System.Text.RegularExpressions;
using Valour.Server.Database;
using Valour.Server.Workers;
using Valour.Shared;
using Valour.Shared.Models;
using Valour.Shared.Models.Threads;

namespace Valour.Server.Services;

public class NotificationService
{
    private readonly ValourDb _db;
    private readonly CoreHubService _coreHub;
    private readonly NodeLifecycleService _nodeLifecycleService;
    private readonly ILogger<NotificationService> _logger;
    private readonly PushNotificationWorker _pushNotificationWorker;
    private readonly HostedPlanetService _hostedService;
    private readonly ChannelWatchingService _channelWatchingService;
    private readonly PlanetPermissionService _permissionService;

    public NotificationService(
        ValourDb db,
        CoreHubService coreHub,
        NodeLifecycleService nodeLifecycleService,
        ILogger<NotificationService> logger,
        PushNotificationWorker pushNotificationWorker,
        HostedPlanetService hostedService,
        ChannelWatchingService channelWatchingService,
        PlanetPermissionService permissionService)
    {
        _db = db;
        _coreHub = coreHub;
        _nodeLifecycleService = nodeLifecycleService;
        _logger = logger;
        _pushNotificationWorker = pushNotificationWorker;
        _hostedService = hostedService;
        _channelWatchingService = channelWatchingService;
        _permissionService = permissionService;
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

    /// <summary>
    /// Permanently deletes read notifications older than the given age
    /// </summary>
    public async Task<int> DeleteOldReadNotificationsAsync(TimeSpan maxAge)
    {
        var cutoff = DateTime.UtcNow - maxAge;
        return await _db.Notifications
            .Where(x => x.TimeRead != null && x.TimeRead < cutoff)
            .ExecuteDeleteAsync();
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
    
    /// <summary>
    /// Upper bound on unread notifications hydrated at once
    /// </summary>
    const int MaxUnreadFetch = 99;

    public async Task<List<Models.Notification>> GetAllUnreadNotifications(long userId)
        => await _db.Notifications.Where(x => x.UserId == userId && x.TimeRead == null)
            .OrderByDescending(x => x.TimeSent)
            .Take(MaxUnreadFetch)
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

        if (await ShouldSendPushNotificationAsync(userId, notification))
        {
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

        var membersWithRole = await _db.PlanetMembers
            .AsNoTracking()
            .WithRoleByLocalIndex(hostedPlanet.Planet.Id,  role.FlagBitIndex)
            .Select(x => new { x.Id, x.UserId })
            .ToArrayAsync();

        // Only notify members who can actually view the channel (#1570)
        var allowedUserIds = new HashSet<long>();
        foreach (var memberWithRole in membersWithRole)
        {
            if (baseNotification.ChannelId is null ||
                await _permissionService.HasChannelAccessAsync(memberWithRole.Id, baseNotification.ChannelId.Value))
                allowedUserIds.Add(memberWithRole.UserId);
        }

        var userIds = allowedUserIds.ToArray();

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

        var pushUserIds = await FilterPushRecipientsAsync(filteredUserIds, baseNotification.ChannelId);
        if (pushUserIds.Length > 0)
        {
            await _pushNotificationWorker.QueueNotificationAction(new SendUsersPushNotification()
            {
                UserIds = pushUserIds,
                Content = pushContent
            });
        }

        _logger.LogInformation(
            "Queued role mention notifications for {RecipientCount} users and push notifications for {PushRecipientCount} users on role {RoleId}",
            filteredUserIds.Length,
            pushUserIds.Length,
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
        var replySenderName = member is null || string.IsNullOrWhiteSpace(member.Nickname)
            ? user.Name
            : member.Nickname;

        Models.Notification notification = new()
        {
            Title = replySenderName + " in " + channel.Name + (planet is null ? "" : $" ({planet.Name})"),
            Body = await ReplaceMentionTagsAsync(message.Content),
            ImageUrl = member is null ? user.GetAvatar() : member.GetAvatar(),
            ClickUrl = planet is null ? 
                $"/directchannels/{channel.Id}/{message.Id}" : 
                $"/planetchannels/{planet.Id}/{channel.Id}/{message.Id}",
            PlanetId = planet?.Id,
            ChannelId = channel.Id,
            Source = planet is null ? NotificationSource.DirectReply : NotificationSource.PlanetMemberReply,
            SourceId = message.Id,
            UserId = repliedToMessage.AuthorUserId,
        };

        await SendUserNotification(repliedToMessage.AuthorUserId, notification);
    }
    
    /// <summary>
    /// Notifies the relevant author when a thread comment is posted. Top-level comments
    /// notify the thread author; replies notify the parent comment's author.
    /// </summary>
    public async Task HandleThreadCommentAsync(
        ISharedThreadComment comment,
        ISharedPlanetThread thread,
        ISharedThreadComment parentComment)
    {
        var isReply = parentComment is not null;
        var recipientUserId = isReply ? parentComment.AuthorUserId : thread.AuthorUserId;

        // Never notify someone about their own comment
        if (recipientUserId == comment.AuthorUserId)
            return;

        var senderUser = (await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == comment.AuthorUserId))?.ToModel();

        if (senderUser is null)
            return;

        var senderName = senderUser.Name;
        var senderAvatar = ISharedUser.GetAvatar(senderUser, AvatarFormat.Webp128);

        if (comment.AuthorMemberId is not null)
        {
            var memberMeta = await _db.PlanetMembers
                .AsNoTracking()
                .Where(x => x.Id == comment.AuthorMemberId)
                .Select(x => new { x.Nickname, x.MemberAvatar })
                .FirstOrDefaultAsync();

            if (memberMeta is not null)
            {
                if (!string.IsNullOrWhiteSpace(memberMeta.Nickname))
                    senderName = memberMeta.Nickname;
                if (!string.IsNullOrWhiteSpace(memberMeta.MemberAvatar))
                    senderAvatar = memberMeta.MemberAvatar;
            }
        }

        var title = isReply
            ? $"{senderName} replied to your comment"
            : $"{senderName} commented on your post";

        Models.Notification notification = new()
        {
            Title = title,
            Body = string.IsNullOrWhiteSpace(comment.Content) ? thread.Title : comment.Content,
            ImageUrl = senderAvatar,
            ClickUrl = $"/planetthreads/{thread.PlanetId}/{thread.Id}",
            PlanetId = thread.PlanetId,
            Source = isReply ? NotificationSource.ThreadReply : NotificationSource.ThreadComment,
            SourceId = comment.Id,
            UserId = recipientUserId,
        };

        await SendUserNotification(recipientUserId, notification);
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

        var dmBody = await ReplaceMentionTagsAsync(message.Content);

        foreach (var recipientId in recipientIds)
        {
            Models.Notification notification = new()
            {
                Title = user.Name + " DMed you.",
                Body = dmBody,
                ImageUrl = user.GetAvatar(),
                ClickUrl = $"/directchannels/{channel.Id}/{message.Id}",
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
        ISharedPlanet? planet,
        ISharedMessage message,
        ISharedPlanetMember? member,
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
    
    private static readonly Regex MentionTagRegex = new(@"«@([umrc])-(\d+)»", RegexOptions.Compiled);

    /// <summary>
    /// Replaces all mention tags («@m-123», «@u-123», «@r-123», «@c-123») in message
    /// content with readable names so notifications don't show raw tags or bare '@'.
    /// </summary>
    private async Task<string> ReplaceMentionTagsAsync(string? content)
    {
        if (string.IsNullOrEmpty(content) || !content.Contains('«'))
            return content ?? string.Empty;

        foreach (var match in MentionTagRegex.Matches(content).DistinctBy(x => x.Value))
        {
            var type = match.Groups[1].Value[0];
            if (!long.TryParse(match.Groups[2].Value, out var targetId))
                continue;

            string? name = null;
            try
            {
                switch (type)
                {
                    case 'u':
                        name = await _db.Users.AsNoTracking()
                            .Where(x => x.Id == targetId)
                            .Select(x => x.Name)
                            .FirstOrDefaultAsync();
                        break;
                    case 'm':
                        var memberNames = await _db.PlanetMembers.AsNoTracking()
                            .Where(x => x.Id == targetId)
                            .Select(x => new { x.Nickname, UserName = x.User.Name })
                            .FirstOrDefaultAsync();
                        name = string.IsNullOrWhiteSpace(memberNames?.Nickname)
                            ? memberNames?.UserName
                            : memberNames.Nickname;
                        break;
                    case 'r':
                        name = await _db.PlanetRoles.AsNoTracking()
                            .Where(x => x.Id == targetId)
                            .Select(x => x.Name)
                            .FirstOrDefaultAsync();
                        break;
                    case 'c':
                        name = await _db.Channels.AsNoTracking()
                            .Where(x => x.Id == targetId)
                            .Select(x => x.Name)
                            .FirstOrDefaultAsync();
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to resolve mention tag {Tag} for notification", match.Value);
            }

            if (string.IsNullOrWhiteSpace(name))
                name = type switch { 'r' => "role", 'c' => "channel", _ => "user" };

            var prefix = type == 'c' ? "#" : "@";
            content = content.Replace(match.Value, prefix + name);
        }

        return content;
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
        if (mentionTargetUser is null)
            return;

        var content = await ReplaceMentionTagsAsync(message.Content);

        Models.Notification notification = new()
        {
            Title = user.Name + " mentioned you in DMs",
            Body = content,
            ImageUrl = ISharedUser.GetAvatar(user, AvatarFormat.Webp128),
            ClickUrl = $"/directchannels/{channel.Id}/{message.Id}",
            ChannelId = channel.Id,
            Source = NotificationSource.DirectMention,
            SourceId = message.Id,
            UserId = mentionTargetUser.Id,
        };
        
        await SendUserNotification(mentionTargetUser.Id, notification);
    }

    private async Task HandleMemberMentionAsync(
        Mention mention,
        ISharedPlanet? planet,
        ISharedMessage message,
        ISharedPlanetMember? member,
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

        // The mention must target a member of the planet the message was posted in
        if (targetMember.PlanetId != planet.Id)
            return;

        // Don't notify members who cannot view the channel (#1570)
        if (!await _permissionService.HasChannelAccessAsync(targetMember.Id, channel.Id))
            return;

        var content = await ReplaceMentionTagsAsync(message.Content);

        // System-originated messages (for example Automod responses authored
        // by Victor) intentionally have no PlanetMember. They may still
        // mention a member, so do not let notification formatting abort the
        // message post before it reaches the queue.
        var senderName = string.IsNullOrWhiteSpace(member?.Nickname) ? user.Name : member.Nickname;
        var title = user.Id == ISharedUser.VictorUserId
            ? "Victor in " + planet.Name
            : senderName + " in " + planet.Name;
        
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
            ClickUrl = $"/planetchannels/{planet.Id}/{channel.Id}/{message.Id}",
            TimeSent = DateTime.UtcNow
        };

        await SendUserNotification(targetMember.UserId, notification);
    }

    private async Task HandleRoleMentionAsync(
        Mention mention,
        ISharedPlanet? planet,
        ISharedMessage message,
        ISharedPlanetMember? member,
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

        var content = await ReplaceMentionTagsAsync(message.Content);
        var mentionSource = GetRoleMentionSource(targetRole);

        var roleSenderName = string.IsNullOrWhiteSpace(member?.Nickname) ? user.Name : member.Nickname;
        var baseNotification = new Notification()
        {
            Id = Guid.NewGuid(),
            Title = roleSenderName + " in " + planet.Name,
            Body = content,
            ImageUrl = ISharedUser.GetAvatar(user, AvatarFormat.Webp128),
            PlanetId = planet.Id,
            ChannelId = channel.Id,
            Source = mentionSource,
            SourceId = message.Id,
            ClickUrl = $"/planetchannels/{planet.Id}/{channel.Id}/{message.Id}"
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

    private async Task<bool> ShouldSendPushNotificationAsync(long userId, Models.Notification notification)
    {
        if (notification.ChannelId is null)
            return true;

        try
        {
            return !await _channelWatchingService.IsUserViewingChannelAsync(userId, notification.ChannelId.Value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed checking active channel view state for user {UserId} in channel {ChannelId}",
                userId,
                notification.ChannelId.Value);
            return true;
        }
    }

    private async Task<long[]> FilterPushRecipientsAsync(long[] userIds, long? channelId)
    {
        if (channelId is null || userIds.Length == 0)
            return userIds;

        try
        {
            return await _channelWatchingService.FilterUsersNotViewingChannelAsync(channelId.Value, userIds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed filtering active channel viewers for channel {ChannelId}",
                channelId.Value);
            return userIds;
        }
    }
}
