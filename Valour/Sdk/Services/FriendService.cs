using Valour.Sdk.Client;
using Valour.Sdk.ModelLogic;
using Valour.Shared;
using Valour.Shared.Models;
using Valour.Shared.Utilities;

namespace Valour.Sdk.Services;

public class FriendService : ServiceBase
{
    public HybridEvent<FriendEventData> FriendsChanged;

    // Users you are friends with (mutual)
    public readonly List<User> Friends = new();
    public readonly Dictionary<long, User> FriendLookup = new();

    // Users you have sent a friend request to (pending)
    public readonly List<User> OutgoingRequests = new();

    // Users who have sent a friend request to you (pending)
    public readonly List<User> IncomingRequests = new();

    private static readonly LogOptions LogOptions = new(
        "FriendService",
        "#036bfc",
        "#fc0356",
        "#fc8403"
    );

    private readonly ValourClient _client;
    private readonly CacheService _cache;
    private readonly object _lock = new();

    public FriendService(ValourClient client)
    {
        _client = client;
        _cache = client.Cache;
        SetupLogging(client.Logger, LogOptions);
    }

    /// <summary>
    /// Fetches friend data from the server
    /// </summary>
    public async Task FetchFriendsAsync()
    {
        var data = await _client.Me.FetchFriendDataAsync();
        if (data is null)
        {
            LogError("Error loading friends.");
            return;
        }

        lock (_lock)
        {
            IncomingRequests.Clear();
            OutgoingRequests.Clear();
            Friends.Clear();
            FriendLookup.Clear();

            // Outgoing: requests you sent
            foreach (var added in data.Added)
            {
                if (!OutgoingRequests.Any(x => x.Id == added.Id))
                    OutgoingRequests.Add(added);
            }

            // Incoming: requests you received
            foreach (var addedBy in data.AddedBy)
            {
                if (!IncomingRequests.Any(x => x.Id == addedBy.Id))
                    IncomingRequests.Add(addedBy);
            }

            // Move mutual requests to Friends
            foreach (var req in OutgoingRequests.ToList())
            {
                if (IncomingRequests.Any(x => x.Id == req.Id))
                {
                    if (!Friends.Any(x => x.Id == req.Id))
                        Friends.Add(req);
                    if (!FriendLookup.ContainsKey(req.Id))
                        FriendLookup[req.Id] = req;

                    OutgoingRequests.RemoveAll(x => x.Id == req.Id);
                    IncomingRequests.RemoveAll(x => x.Id == req.Id);
                }
            }
        }

        Log($"Loaded {Friends.Count} friends.");

        FriendsChanged?.Invoke(new FriendEventData()
        {
            User = null,
            Type = FriendEventType.FetchedAll
        });
    }

    public void OnFriendEventReceived(FriendEventData eventData)
    {
        if (eventData?.User == null)
            return;

        lock (_lock)
        {
            var user = eventData.User;
            switch (eventData.Type)
            {
                case FriendEventType.AddedMe:
                    // Someone sent you a request
                    if (OutgoingRequests.Any(x => x.Id == user.Id))
                    {
                        // We already sent them a request, so now we're mutual friends
                        OutgoingRequests.RemoveAll(x => x.Id == user.Id);
                        IncomingRequests.RemoveAll(x => x.Id == user.Id);
                        if (!Friends.Any(x => x.Id == user.Id))
                        {
                            Friends.Add(user);
                            FriendLookup[user.Id] = user;
                        }
                    }
                    else if (!IncomingRequests.Any(x => x.Id == user.Id))
                    {
                        // New incoming request
                        IncomingRequests.Add(user);
                    }
                    break;

                case FriendEventType.RemovedMe:
                    // Someone removed us as a friend
                    Friends.RemoveAll(x => x.Id == user.Id);
                    FriendLookup.Remove(user.Id);
                    IncomingRequests.RemoveAll(x => x.Id == user.Id);
                    OutgoingRequests.RemoveAll(x => x.Id == user.Id);
                    break;

                case FriendEventType.DeclinedMe:
                    // Someone declined our friend request
                    OutgoingRequests.RemoveAll(x => x.Id == user.Id);
                    break;

                case FriendEventType.CancelledMe:
                    // Someone cancelled their request to us
                    IncomingRequests.RemoveAll(x => x.Id == user.Id);
                    break;
            }
        }

        FriendsChanged?.Invoke(eventData);
    }

    /// <summary>
    /// Adds a friend (send request)
    /// </summary>
    public async Task<TaskResult<UserFriend>> AddFriendAsync(string nameAndTag)
    {
        var result = await _client.PrimaryNode.PostAsyncWithResponse<UserFriend>(
            $"api/userfriends/add/{Uri.EscapeDataString(nameAndTag)}");

        if (!result.Success)
            return result;

        var addedUser = await _client.UserService.FetchUserAsync(result.Data.FriendId);

        lock (_lock)
        {
            // If they already sent us a request, we're now mutual friends
            if (IncomingRequests.Any(x => x.Id == addedUser.Id))
            {
                IncomingRequests.RemoveAll(x => x.Id == addedUser.Id);
                OutgoingRequests.RemoveAll(x => x.Id == addedUser.Id);

                if (!Friends.Any(x => x.Id == addedUser.Id))
                    Friends.Add(addedUser);

                FriendLookup[addedUser.Id] = addedUser;
            }
            else
            {
                // Otherwise just an outgoing request
                if (!OutgoingRequests.Any(x => x.Id == addedUser.Id))
                    OutgoingRequests.Add(addedUser);
            }
        }

        var eventData = new FriendEventData
        {
            User = addedUser,
            Type = FriendEventType.AddedThem
        };

        FriendsChanged?.Invoke(eventData);

        return result;
    }

    /// <summary>
    /// Declines a friend request (from someone else)
    /// </summary>
    public async Task<TaskResult> DeclineFriendAsync(string nameAndTag)
    {
        var result = await _client.PrimaryNode.PostAsync(
            $"api/userfriends/decline/{Uri.EscapeDataString(nameAndTag)}", null);

        if (!result.Success)
            return result;

        User declined = null;
        lock (_lock)
        {
            declined = IncomingRequests.FirstOrDefault(
                x => x.NameAndTag.Equals(nameAndTag, StringComparison.OrdinalIgnoreCase));
            if (declined is not null)
                IncomingRequests.Remove(declined);
        }

        var eventData = new FriendEventData
        {
            User = declined,
            Type = FriendEventType.DeclinedThem
        };

        FriendsChanged?.Invoke(eventData);

        return result;
    }

    /// <summary>
    /// Removes a friend
    /// </summary>
    public async Task<TaskResult> RemoveFriendAsync(string nameAndTag)
    {
        var result = await _client.PrimaryNode.PostAsync(
            $"api/userfriends/remove/{Uri.EscapeDataString(nameAndTag)}", null);

        if (!result.Success)
            return result;

        User friend = null;
        lock (_lock)
        {
            IncomingRequests.RemoveAll(
                x => x.NameAndTag.Equals(nameAndTag, StringComparison.OrdinalIgnoreCase));
            friend = Friends.FirstOrDefault(
                x => x.NameAndTag.Equals(nameAndTag, StringComparison.OrdinalIgnoreCase));
            if (friend is not null)
            {
                Friends.Remove(friend);
                FriendLookup.Remove(friend.Id);
                if (!OutgoingRequests.Any(x => x.Id == friend.Id))
                    OutgoingRequests.Add(friend);
            }
        }

        var eventData = new FriendEventData()
        {
            User = friend,
            Type = FriendEventType.RemovedThem
        };

        FriendsChanged?.Invoke(eventData);

        return result;
    }

    /// <summary>
    /// Cancels a friend request you sent
    /// </summary>
    public async Task<TaskResult> CancelFriendAsync(string nameAndTag)
    {
        var result = await _client.PrimaryNode.PostAsync(
            $"api/userfriends/cancel/{Uri.EscapeDataString(nameAndTag)}", null);

        if (!result.Success)
            return result;

        User canceled = null;
        lock (_lock)
        {
            canceled = OutgoingRequests.FirstOrDefault(
                x => x.NameAndTag.Equals(nameAndTag, StringComparison.OrdinalIgnoreCase));
            if (canceled is not null)
                OutgoingRequests.Remove(canceled);
        }

        var eventData = new FriendEventData
        {
            User = canceled,
            Type = FriendEventType.CancelledThem
        };

        FriendsChanged?.Invoke(eventData);

        return result;
    }

    public async Task<List<User>> GetFriendsAsync(long userId)
    {
        var result = await _client.PrimaryNode.GetJsonAsync<List<User>>(
            $"{ISharedUser.GetIdRoute(userId)}/friends");

        if (!result.Success || result.Data == null)
            return new List<User>();

        result.Data.SyncAll(_client);

        return result.Data;
    }

    public async Task<UserFriendData> FetchFriendDataAsync(long userId)
    {
        var result = await _client.PrimaryNode.GetJsonAsync<UserFriendData>(
            $"{ISharedUser.GetIdRoute(userId)}/frienddata");
        if (!result.Success || result.Data == null)
            return new UserFriendData();

        result.Data.Added.SyncAll(_client);
        result.Data.AddedBy.SyncAll(_client);

        return result.Data;
    }
}
