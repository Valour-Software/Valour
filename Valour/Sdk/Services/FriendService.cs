using System.Web;
using Valour.Sdk.Client;
using Valour.Shared;
using Valour.Shared.Models;
using Valour.Shared.Utilities;

namespace Valour.Sdk.Services;

public class FriendService : ServiceBase
{
    /// <summary>
    /// Run when there is a friend event
    /// </summary>
    public HybridEvent<FriendEventData> FriendsChanged;

    /// <summary>
    /// The friends of this client
    /// </summary>
    public readonly List<User> Friends = new();

    /// <summary>
    /// The fast lookup set for friends
    /// </summary>
    public readonly Dictionary<long, User> FriendLookup = new();
    public readonly List<User> FriendRequests = new();
    public readonly List<User> FriendsRequested = new ();
    
    private static readonly LogOptions LogOptions = new (
	    "FriendService",
	    "#036bfc",
	    "#fc0356",
	    "#fc8403"
    );
    
    private readonly ValourClient _client;
    private readonly CacheService _cache;
    public FriendService(ValourClient client)
    {
	    _client = client;
	    _cache = client.Cache;
	    SetupLogging(client.Logger, LogOptions);
    }
    
    /// <summary>
    /// Fetches friend data from the server
    /// </summary>
    public async Task FetchesFriendsAsync()
    {
	    var data = await _client.Me.FetchFriendDataAsync();
	    if (data is null)
	    {
		    LogError("Error loading friends.");
		    return;
	    }
	    
	    FriendRequests.Clear();
	    FriendsRequested.Clear();

	    foreach (var added in data.Added)
		    FriendRequests.Add(added);

	    foreach (var addedBy in data.AddedBy)
		    FriendsRequested.Add(addedBy);

	    Friends.Clear();
	    FriendLookup.Clear();

	    foreach (var req in FriendRequests)
	    {
		    if (FriendsRequested.Any(x => x.Id == req.Id))
		    {
			    Friends.Add(req);
			    FriendLookup.Add(req.Id, req);
		    }
	    }

	    foreach (var friend in Friends)
	    {
		    FriendRequests.RemoveAll(x => x.Id == friend.Id);
		    FriendsRequested.RemoveAll(x => x.Id == friend.Id);
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
        if (eventData.Type == FriendEventType.AddedMe)
        {
            // If we already had a friend request to them,
            if (FriendsRequested.Any(x => x.Id == eventData.User.Id))
            {
                // Add as a friend
                Friends.Add(eventData.User);
            }
            // otherwise,
            else
            {
                // add to friend requests
                FriendRequests.Add(eventData.User);
            }
        } 
        else if (eventData.Type == FriendEventType.RemovedMe)
        {
            // If they were already a friend,
            if (Friends.Any(x => x.Id == eventData.User.Id))
            {
                // remove them
                Friends.RemoveAll(x => x.Id == eventData.User.Id);
            }
            // otherwise,
            else
            {
                // remove from friend requests
                FriendRequests.RemoveAll(x => x.Id == eventData.User.Id);
            }
        }
        
        FriendsChanged?.Invoke(eventData);
    }

    /// <summary>
    /// Adds a friend
    /// </summary>
    public async Task<TaskResult<UserFriend>> AddFriendAsync(string nameAndTag)
    {
        var result = await _client.PrimaryNode.PostAsyncWithResponse<UserFriend>($"api/userfriends/add/{HttpUtility.UrlEncode(nameAndTag)}");

        if (!result.Success)
	        return result;
        
        var addedUser = await _client.UserService.FetchUserAsync(result.Data.FriendId);

		// If we already had a friend request from them,
		// add them to the friends list
		var request = FriendRequests.FirstOrDefault(x => x.NameAndTag.Equals(nameAndTag, StringComparison.OrdinalIgnoreCase));
        if (request is not null)
        {
            FriendRequests.Remove(request);
			Friends.Add(addedUser);
            FriendLookup.Add(addedUser.Id, addedUser);
		}
		// Otherwise, add this request to our request list
		else
		{
            FriendsRequested.Add(addedUser);
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
	/// Declines a friend request
	/// </summary>
	public async Task<TaskResult> DeclineFriendAsync(string nameAndTag)
	{
		var result = await _client.PrimaryNode.PostAsync($"api/userfriends/decline/{HttpUtility.UrlEncode(nameAndTag)}", null);

		if (!result.Success)
			return result;
        
        var declined = FriendRequests.FirstOrDefault(x => x.NameAndTag.ToLower() == nameAndTag.ToLower());
        if (declined is not null)
            FriendRequests.Remove(declined);
        
        var eventData = new FriendEventData
		{
			User = declined,
			Type = FriendEventType.DeclinedThem
		};
        
        FriendsChanged?.Invoke(eventData);

		return result;
	}

	/// <summary>
	/// Removes a friend :(
	/// </summary>
	public async Task<TaskResult> RemoveFriendAsync(string nameAndTag)
    {
        var result = await _client.PrimaryNode.PostAsync($"api/userfriends/remove/{HttpUtility.UrlEncode(nameAndTag)}", null);

        if (!result.Success)
			return result;
        
        FriendsRequested.RemoveAll(x => x.NameAndTag.ToLower() == nameAndTag.ToLower());
        var friend = Friends.FirstOrDefault(x => x.NameAndTag.ToLower() == nameAndTag.ToLower());
        if (friend is not null)
        {
            Friends.Remove(friend);
            FriendLookup.Remove(friend.Id);

            FriendRequests.Add(friend);
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
	/// Cancels a friend request
	/// </summary>
	public async Task<TaskResult> CancelFriendAsync(string nameAndTag)
	{
		var result = await _client.PrimaryNode.PostAsync($"api/userfriends/cancel/{HttpUtility.UrlEncode(nameAndTag)}", null);

		if (!result.Success)
			return result;
		
		var canceled = FriendsRequested.FirstOrDefault(x => x.NameAndTag.ToLower() == nameAndTag.ToLower());
		if (canceled is not null)
			FriendsRequested.Remove(canceled);
		
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
		var result = await _client.PrimaryNode.GetJsonAsync<List<User>>($"{ISharedUser.GetIdRoute(userId)}/friends");

		for (int i = 0; i < result.Data.Count; i++)
		{
			var user = result.Data[i];
			result.Data[i] = _cache.Sync(user);
		}
		
		return result.Data;
	}

	public async Task<UserFriendData> FetchFriendDataAsync(long userId)
	{
		var result = await _client.PrimaryNode.GetJsonAsync<UserFriendData>($"{ISharedUser.GetIdRoute(userId)}/frienddata");
		if (!result.Success)
			return null;
		
		for (int i = 0; i < result.Data.Added.Count; i++)
		{
			var user = result.Data.Added[i];
			result.Data.Added[i] = _cache.Sync(user);
		}
		
		for (int i = 0; i < result.Data.AddedBy.Count; i++)
		{
			var user = result.Data.AddedBy[i];
			result.Data.AddedBy[i] = _cache.Sync(user);
		}
		
		return result.Data;
	}
}