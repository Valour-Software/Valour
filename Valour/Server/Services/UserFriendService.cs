using Valour.Server.Database;
using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Server.Services;

public class UserFriendService
{
    private readonly ValourDb _db;
    private readonly UserService _userService;
    private readonly NotificationService _notificationService;
    private readonly CoreHubService _coreHub;
    private readonly NodeLifecycleService _nodeLifecycleService;
    private readonly ILogger<UserFriendService> _logger;

    public UserFriendService(
        ValourDb db,
        UserService userService,
        NotificationService notificationService,
        CoreHubService coreHub,
        NodeLifecycleService nodeLifecycleService,
        ILogger<UserFriendService> logger)
    {
        _db = db;
        _logger = logger;
        _userService = userService;
        _notificationService = notificationService;
        _coreHub = coreHub;
        _nodeLifecycleService = nodeLifecycleService;
    }

    public async Task<UserFriend> GetAsync(long userId, long friendId) =>
        (await _db.UserFriends.FirstOrDefaultAsync(x => x.UserId == userId &&
                                                   x.FriendId == friendId)).ToModel();

    public async Task<TaskResult> RemoveFriendAsync(string friendUsername, long userId)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user is null)
            return new(false, "User not found.");
        
        var friendUser = await _userService.GetByNameAndTagAsync(friendUsername);
        if (friendUser is null)
            return new(false, $"User {friendUsername} was not found.");

        var friend = await _db.UserFriends.FirstOrDefaultAsync(x => x.UserId == userId &&
                                                                   x.FriendId == friendUser.Id);
        if (friend is null)
            return new(false, "User is already not a friend.");

        _db.UserFriends.Remove(friend);
        await _db.SaveChangesAsync();
        
        await _coreHub.RelayFriendEvent(friendUser.Id, new FriendEventData()
        {
            User = user.ToModel(),
            Type = FriendEventType.RemovedMe
        }, _nodeLifecycleService);

        return new(true, "Success");
    }

    public async Task<TaskResult<UserFriend>> AddFriendAsync(long userId, long friendId)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user is null)
            return new(false, "User not found.");
        
        if (await _db.UserFriends.AnyAsync(x => x.UserId == userId &&
                                                x.FriendId == friendId))
            return new(false, "Friend already added.");
        
        UserFriend newFriend = new()
        {
            Id = IdManager.Generate(),
            UserId = userId,
            FriendId = friendId
        };

        await _db.UserFriends.AddAsync(newFriend.ToDatabase());
        await _db.SaveChangesAsync();
        
        // Send out notification

        var notification = new Notification()
        {
            Title = $"{user.Name} has added you as a friend!",
            Body = "You can add them back or ignore this.",
            ImageUrl = user.GetAvatarUrl(AvatarFormat.Webp128),
            UserId = friendId,
            PlanetId = null,
            ChannelId = null,
            SourceId = userId,
            Source = NotificationSource.FriendRequest,
            ClickUrl = $"/friends"
        };

        await _notificationService.SendUserNotification(friendId, notification);
        
        await _coreHub.RelayFriendEvent(friendId, new FriendEventData()
        {
            User = user.ToModel(),
            Type = FriendEventType.AddedMe
        }, _nodeLifecycleService);

        return new(true, "Success", newFriend);
    }

    public async Task<TaskResult<UserFriend>> AddFriendAsync(string friendUsername, long userId)
    {
        var friendUser = await _userService.GetByNameAndTagAsync(friendUsername);
        if (friendUser is null)
            return new(false, $"User {friendUsername} was not found.");

        return await AddFriendAsync(userId, friendUser.Id);
    }

    public async Task<TaskResult> DeclineRequestAsync(string username, long userId)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user is null)
            return new(false, "User not found.");

        var requestUser = await _userService.GetByNameAndTagAsync(username);
        if (requestUser is null)
            return new(false, $"User {username} was not found.");

        var request = await _db.UserFriends
            .FirstOrDefaultAsync(x => x.UserId == requestUser.Id &&
                                      x.FriendId == userId);

        if (request is null)
            return new(false, $"Friend request was not found.");

        _db.UserFriends.Remove(request);
        await _db.SaveChangesAsync();

        await _coreHub.RelayFriendEvent(requestUser.Id, new FriendEventData()
        {
            User = user.ToModel(),
            Type = FriendEventType.DeclinedMe
        }, _nodeLifecycleService);

        return new(true, "Success");
    }

    public async Task<TaskResult> CancelRequestAsync(string username, long userId)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user is null)
            return new(false, "User not found.");
        
        var targetUser = await _userService.GetByNameAndTagAsync(username);
        if (targetUser is null)
            return new(false, $"User {username} was not found.");

        var request = await _db.UserFriends
            .FirstOrDefaultAsync(x => x.UserId == userId &&
                                      x.FriendId == targetUser.Id);

        if (request is null)
            return new(false, $"Friend request was not found.");

        _db.UserFriends.Remove(request);
        await _db.SaveChangesAsync();
        
        await _coreHub.RelayFriendEvent(targetUser.Id, new FriendEventData()
        {
            User = user.ToModel(),
            Type = FriendEventType.CancelledMe
        }, _nodeLifecycleService);

        return new(true, "Success");
    }
}