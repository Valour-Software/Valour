using Newtonsoft.Json.Linq;
using System.Security.Cryptography;
using Valour.Server.Database;
using Valour.Shared;

namespace Valour.Server.Services;

public class UserFriendService
{
    private readonly ValourDB _db;
    private readonly ILogger<UserFriendService> _logger;

    public UserFriendService(
        ValourDB db,
        ILogger<UserFriendService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<UserFriend> GetAsync(long userId, long friendId) =>
        (await _db.UserFriends.FirstOrDefaultAsync(x => x.UserId == userId &&
                                                   x.FriendId == friendId)).ToModel();

    public async Task<TaskResult> RemoveFriendAsync(string friendUsername, long userId)
    {
        var friendUser = await _db.Users.FirstOrDefaultAsync(x => x.Name.ToLower() == friendUsername.ToLower());
        if (friendUser is null)
            return new(false, $"User {friendUsername} was not found.");

        var friend = await _db.UserFriends.FirstOrDefaultAsync(x => x.UserId == userId &&
                                                                   x.FriendId == friendUser.Id);
        if (friend is null)
            return new(false, "User is already not a friend.");

        _db.UserFriends.Remove(friend);
        await _db.SaveChangesAsync();

        return new(true, "Succcess");
    }

    public async Task<TaskResult<UserFriend>> AddFriendAsync(long friendId, long userId)
    {
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

        return new(true, "Success", newFriend);
    }

    public async Task<TaskResult<UserFriend>> AddFriendAsync(string friendUsername, long userId)
    {
        var friendUser = await _db.Users.FirstOrDefaultAsync(x => x.Name.ToLower() == friendUsername.ToLower());
        if (friendUser is null)
            return new(false, $"User {friendUsername} was not found.");

        return await AddFriendAsync(friendUser.Id, userId);
    }

    public async Task<TaskResult> DeclineRequestAsync(string username, long userId)
    {
        var requestUser = await _db.Users.FirstOrDefaultAsync(x => x.Name.ToLower() == username.ToLower());
        if (requestUser is null)
            return new(false, $"User {username} was not found.");

        var request = await _db.UserFriends
            .FirstOrDefaultAsync(x => x.UserId == requestUser.Id &&
                                      x.FriendId == userId);

        if (request is null)
            return new(false, $"Friend request was not found.");

        _db.UserFriends.Remove(request);
        await _db.SaveChangesAsync();

        return new(true, "Success");
    }

    public async Task<TaskResult> CancelRequestAsync(string username, long userId)
    {
        var targetUser = await _db.Users.FirstOrDefaultAsync(x => x.Name.ToLower() == username.ToLower());
        if (targetUser is null)
            return new(false, $"User {username} was not found.");

        var request = await _db.UserFriends
            .FirstOrDefaultAsync(x => x.UserId == userId &&
                                      x.FriendId == targetUser.Id);

        if (request is null)
            return new(false, $"Friend request was not found.");

        _db.UserFriends.Remove(request);
        await _db.SaveChangesAsync();

        return new(true, "Success");
    }
}