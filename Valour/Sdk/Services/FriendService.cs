using System.Web;
using Valour.Sdk.Client;
using Valour.Shared;
using Valour.Shared.Utilities;

namespace Valour.SDK.Services;

public static class FriendService
{
    /// <summary>
    /// Run when there is a friend event
    /// </summary>
    public static HybridEvent<FriendEventData> FriendsUpdated;
    
    /// <summary>
    /// The friends of this client
    /// </summary>
    public static List<User> Friends { get; set; }

    /// <summary>
    /// The fast lookup set for friends
    /// </summary>
    public static Dictionary<long, User> FriendLookup { get; set; }
    public static List<User> FriendRequests { get; set; }
    public static List<User> FriendsRequested { get; set; }
    
    public static async Task LoadFriendsAsync()
    {
	    var friendResult = await ValourClient.Self.GetFriendDataAsync();

	    if (!friendResult.Success)
	    {
		    await Logger.Log("Error loading friends.", "red");
		    await Logger.Log(friendResult.Message, "red");
		    return;
	    }

	    var data = friendResult.Data;
	    
	    var cachedAdded = new List<User>();
	    var cachedAddedBy = new List<User>();

	    foreach (var added in data.Added)
		    cachedAdded.Add(added.Sync());

	    foreach (var addedBy in data.AddedBy)
		    cachedAddedBy.Add(addedBy.Sync());

	    Friends = new();
	    FriendLookup = new();
	    FriendRequests = cachedAddedBy;
	    FriendsRequested = cachedAdded;

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

	    await Logger.Log($"Loaded {Friends.Count} friends.", "cyan");
    }
    
    public static void HandleFriendEventReceived(FriendEventData eventData)
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
        
        FriendsUpdated?.Invoke(eventData);
    }

    /// <summary>
    /// Adds a friend
    /// </summary>
    public static async Task<TaskResult<UserFriend>> AddFriendAsync(string nameAndTag)
    {
        var result = await ValourClient.PrimaryNode.PostAsyncWithResponse<UserFriend>($"api/userfriends/add/{HttpUtility.UrlEncode(nameAndTag)}");

        if (!result.Success)
	        return result;
        
        var addedUser = await User.FindAsync(result.Data.FriendId);

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
            
		FriendsUpdated?.Invoke(eventData);
		
        return result;
    }

	/// <summary>
	/// Declines a friend request
	/// </summary>
	public static async Task<TaskResult> DeclineFriendAsync(string nameAndTag)
	{
		var result = await ValourClient.PrimaryNode.PostAsync($"api/userfriends/decline/{HttpUtility.UrlEncode(nameAndTag)}", null);

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
        
        FriendsUpdated?.Invoke(eventData);

		return result;
	}

	/// <summary>
	/// Removes a friend :(
	/// </summary>
	public static async Task<TaskResult> RemoveFriendAsync(string nameAndTag)
    {
        var result = await ValourClient.PrimaryNode.PostAsync($"api/userfriends/remove/{HttpUtility.UrlEncode(nameAndTag)}", null);

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
        
        FriendsUpdated?.Invoke(eventData);
        
        return result;
    }

	/// <summary>
	/// Cancels a friend request
	/// </summary>
	public static async Task<TaskResult> CancelFriendAsync(string nameAndTag)
	{
		var result = await ValourClient.PrimaryNode.PostAsync($"api/userfriends/cancel/{HttpUtility.UrlEncode(nameAndTag)}", null);

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
		
		FriendsUpdated?.Invoke(eventData);

		return result;
	}
}