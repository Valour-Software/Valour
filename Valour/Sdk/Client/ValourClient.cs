using Microsoft.AspNetCore.SignalR.Client;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using Valour.Sdk.Extensions;
using Valour.Sdk.Models.Messages.Embeds;
using Valour.Sdk.Models.Economy;
using Valour.Sdk.Nodes;
using Valour.Shared;
using Valour.Shared.Channels;
using Valour.Shared.Models;

namespace Valour.Sdk.Client;

/*
     █████   █████           ████                               
    ░░███   ░░███           ░░███                                   
     ░███    ░███   ██████   ░███   ██████  █████ ████ ████████ 
     ░███    ░███  ░░░░░███  ░███  ███░░███░░███ ░███ ░░███░░███
     ░░███   ███    ███████  ░███ ░███ ░███ ░███ ░███  ░███ ░░░ 
      ░░░█████░    ███░░███  ░███ ░███ ░███ ░███ ░███  ░███     
        ░░███     ░░████████ █████░░██████  ░░████████ █████    
         ░░░       ░░░░░░░░ ░░░░░  ░░░░░░    ░░░░░░░░ ░░░░░     
                                                            
    This is the client-side API for Valour. It is used to connect to nodes and
    interact with the Valour network. This client is helpful for building bots,
    but make sure you follow the platform terms of use, which should be included
    in this repository.                
 */

public static class ValourClient
{

    // If things aren't working and you don't know why in dev...
    // this is probably why.

#if (!DEBUG)
    public static string BaseAddress = "https://app.valour.gg/";
#else
    // public static string BaseAddress = "https://app.valour.gg/";
    // public static string BaseAddress = "http://192.168.1.183:5000/";
    public static string BaseAddress = "https://localhost:5001/";
#endif

    /// <summary>
    /// The user for this client instance
    /// </summary>
    public static User Self { get; set; }

    /// <summary>
    /// The token for this client instance
    /// </summary>
    public static string Token => _token;

    /// <summary>
    /// The internal token for this client
    /// </summary>
    private static string _token;

    /// <summary>
    /// The planets this client has joined
    /// </summary>
    public static List<Planet> JoinedPlanets;

    /// <summary>
    /// The IDs of the client's joined planets
    /// </summary>
    private static List<long> _joinedPlanetIds;

    /// <summary>
    /// The HttpClient to be used for general request (no node!)
    /// </summary>
    public static HttpClient Http => _httpClient;

    /// <summary>
    /// The internal HttpClient
    /// </summary>
    private static HttpClient _httpClient;

    /// <summary>
    /// True if the client is logged in
    /// </summary>
    public static bool IsLoggedIn => Self != null;

    /// <summary>
    /// Currently opened planets
    /// </summary>
    public static List<Planet> OpenPlanets { get; private set; }
    
    /// <summary>
    /// A set of locks used to prevent planet connections from closing automatically
    /// </summary>
    public static Dictionary<string, long> PlanetLocks { get; private set; } = new();

    /// <summary>
    /// Currently opened channels
    /// </summary>
    public static List<Channel> OpenPlanetChannels { get; private set; }

    /// <summary>
    /// A set of locks used to prevent channel connections from closing automatically
    /// </summary>
    public static Dictionary<string, long> ChannelLocks { get; private set; } = new();
    
    /// <summary>
    /// The state of channels this user has access to
    /// </summary>
    private static readonly Dictionary<long, DateTime?> ChannelsLastViewedState = new();
    
    /// <summary>
    /// The last update times of channels this user has access to
    /// </summary>
    private static readonly Dictionary<long, ChannelState> CurrentChannelStates = new();

    /// <summary>
    /// The primary node this client is connected to
    /// </summary>
    public static Node PrimaryNode { get; set; }

    /// <summary>
    /// The friends of this client
    /// </summary>
    public static List<User> Friends { get; set; }

    /// <summary>
    /// The fast lookup set for friends
    /// </summary>
    public static HashSet<long> FriendFastLookup { get; set; }
    public static List<User> FriendRequests { get; set; }
    public static List<User> FriendsRequested { get; set; }
    
    /// <summary>
    /// Pain and suffering for thee
    /// </summary>
    public static List<Notification> UnreadNotifications { get; set; }
    
    /// <summary>
    /// A set from the source if of notifications to the notification.
    /// Used for extremely efficient lookups.
    /// </summary>
    public static Dictionary<long, Notification> UnreadNotificationsLookup { get; set; }
    
    /// <summary>
    /// The direct chat channels (dms) of this user
    /// </summary>
    public static List<Channel> DirectChatChannels { get; set; }
    
    /// <summary>
    /// The Tenor favorites of this user
    /// </summary>
    public static List<TenorFavorite> TenorFavorites { get; set; }

    #region Event Fields

    /// <summary>
    /// Run when the client browser is refocused
    /// </summary>
    public static event Func<Task> OnRefocus;

    /// <summary>
    /// Run when the friends list updates
    /// </summary>
    public static event Func<Task> OnFriendsUpdate;

    /// <summary>
    /// Run when SignalR opens a planet
    /// </summary>
    public static event Func<Planet, Task> OnPlanetOpen;

    /// <summary>
    /// Run when SignalR closes a planet
    /// </summary>
    public static event Func<Planet, Task> OnPlanetClose;

    /// <summary>
    /// Run when a planet is joined
    /// </summary>
    public static event Func<Planet, Task> OnPlanetJoin;

    /// <summary>
    /// Run when a planet is left
    /// </summary>
    public static event Func<Planet, Task> OnPlanetLeave;

    /// <summary>
    /// Run when a UserChannelState is updated
    /// </summary>
    public static event Func<UserChannelState, Task> OnUserChannelStateUpdate;

    /// <summary>
    /// Run when SignalR opens a channel
    /// </summary>
    public static event Func<Channel, Task> OnChannelOpen;

    /// <summary>
    /// Run when SignalR closes a channel
    /// </summary>
    public static event Func<Channel, Task> OnChannelClose;

    /// <summary>
    /// Run when a message is received
    /// </summary>
    public static event Func<Message, Task> OnMessageReceived;
    
    /// <summary>
    /// Run when a message is edited
    /// </summary>
    public static event Func<Message, Task> OnMessageEdited;

    /// <summary>
    /// Run when a planet is deleted
    /// </summary>
    public static event Func<Message, Task> OnMessageDeleted;

    /// <summary>
    /// Run when a notification is received
    /// </summary>
    public static event Func<Notification, Task> OnNotificationReceived;

    /// <summary>
    /// Run when notifications are cleared
    /// </summary>
    public static event Func<Task> OnNotificationsCleared;

    /// <summary>
    /// Run when a channel sends a watching update
    /// </summary>
    public static event Func<ChannelWatchingUpdate, Task> OnChannelWatchingUpdate;

    /// <summary>
    /// Run when a channel sends a currently typing update
    /// </summary>
    public static event Func<ChannelTypingUpdate, Task> OnChannelCurrentlyTypingUpdate;

    /// <summary>
    /// Run when a personal embed update is received
    /// </summary>
    public static event Func<PersonalEmbedUpdate, Task> OnPersonalEmbedUpdate;

    /// <summary>
    /// Run when a channel embed update is received
    /// </summary>
    public static event Func<ChannelEmbedUpdate, Task> OnChannelEmbedUpdate;
    
    /// <summary>
    /// Run when a channel state updates
    /// </summary>
    public static event Func<ChannelStateUpdate, Task> OnChannelStateUpdate;

    /// <summary>
    /// Run when a category is reordered
    /// </summary>
    public static event Func<CategoryOrderEvent, Task> OnCategoryOrderUpdate;

    /// <summary>
    /// Run when there is a friend event
    /// </summary>
    public static event Func<FriendEventData, Task> OnFriendEvent;

    /// <summary>
    /// Run when the user logs in
    /// </summary>
    public static event Func<Task> OnLogin;

    public static event Func<Task> OnJoinedPlanetsUpdate;

    public static event Func<Node, Task> OnNodeReconnect;

    public static readonly JsonSerializerOptions DefaultJsonOptions = new JsonSerializerOptions()
    {
        PropertyNameCaseInsensitive = true,
    };

#endregion

    static ValourClient()
    {

        // Add victor dummy member
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        ValourCache.Put(long.MaxValue, new PlanetMember()
        {
            Nickname = "Victor",
            Id = long.MaxValue,
            MemberAvatar = "/media/victor-cyan.png"
        });
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

        OpenPlanets = new List<Planet>();
        OpenPlanetChannels = new List<Channel>();
        JoinedPlanets = new List<Planet>();
        PlanetLocks = new();
        UnreadNotifications = new();
        UnreadNotificationsLookup = new();
        
        // Hook top level events
        HookPlanetEvents();
    }

    /// <summary>
    /// Sets the HTTP client
    /// </summary>
    public static void SetHttpClient(HttpClient client) => _httpClient = client;

    /// <summary>
    /// Returns the member for this client's user given a planet
    /// </summary>
    public static ValueTask<PlanetMember> GetSelfMember(Planet planet, bool forceRefresh = false) =>
        GetSelfMember(planet.Id, forceRefresh);

    /// <summary>
    /// Returns the member for this client's user given a planet id
    /// </summary>
    public static ValueTask<PlanetMember> GetSelfMember(long planetId, bool forceRefresh = false) =>
        PlanetMember.FindAsyncByUser(Self.Id, planetId, forceRefresh);

    /// <summary>
    /// Sets the compliance data for the current user
    /// </summary>
    public static async ValueTask<TaskResult> SetComplianceDataAsync(DateTime birthDate, Locality locality)
    {
        var result = await PrimaryNode.PostAsync($"api/users/self/compliance/{birthDate.ToString("s")}/{locality}", null);
        var taskResult = new TaskResult()
        {
            Success = result.Success,
            Message = result.Message
        };

        return taskResult;
    }
    
    public static async Task<TaskResult<List<EcoAccount>>> GetEcoAccountsAsync()
    {
        return await PrimaryNode.GetJsonAsync<List<EcoAccount>>("api/eco/accounts/self");
    }


    public static async Task<List<TenorFavorite>> GetTenorFavoritesAsync()
    {
        if (TenorFavorites is null)
            await LoadTenorFavoritesAsync();

        return TenorFavorites;
    }

    /// <summary>
    /// Sends a message
    /// </summary>
    public static async Task<TaskResult> SendMessage(Message message)
        => await message.PostMessageAsync();

    /// <summary>
    /// Attempts to join the given planet
    /// </summary>
    public static async Task<TaskResult<PlanetMember>> JoinPlanetAsync(Planet planet)
    {
        var result = await PrimaryNode.PostAsyncWithResponse<PlanetMember>($"api/planets/{planet.Id}/discover");

        if (result.Success)
        {
            JoinedPlanets.Add(planet);

            if (OnPlanetJoin is not null)
                await OnPlanetJoin.Invoke(planet);

            if (OnJoinedPlanetsUpdate is not null)
                await OnJoinedPlanetsUpdate.Invoke();
        }

        return result;
    }

    /// <summary>
    /// Attempts to leave the given planet
    /// </summary>
    public static async Task<TaskResult> LeavePlanetAsync(Planet planet)
    {
        // Get member
        var member = await planet.GetMemberByUserAsync(ValourClient.Self.Id);
        var result = await LiveModel.DeleteAsync(member);

        if (result.Success)
        {
            JoinedPlanets.Remove(planet);

            if (OnPlanetLeave is not null)
                await OnPlanetLeave.Invoke(planet);

            if (OnJoinedPlanetsUpdate is not null)
                await OnJoinedPlanetsUpdate.Invoke();
        }

        return result;
    }
    
    public static void SetChannelLastViewedState(long channelId, DateTime lastViewed)
    {
        ChannelsLastViewedState[channelId] = lastViewed;
    }

    public static bool GetPlanetUnreadState(long planetId)
    {
        var channelStates = CurrentChannelStates.Where(x => x.Value.PlanetId == planetId);

        // Console.WriteLine($"[{planetId}] Checking {channelStates.Count()} channels");
        // Console.WriteLine(JsonSerializer.Serialize(channelStates));
        
        foreach (var state in channelStates)
        {
            if (GetChannelUnreadState(state.Key))
                return true;
        }

        return false;
    }

    public static bool GetChannelUnreadState(long channelId)
    {
        if (OpenPlanetChannels.Any(x => x.Id == channelId))
            return false;

        if (!ChannelsLastViewedState.TryGetValue(channelId, out var lastRead))
        {
            return true;
        }

        if (!CurrentChannelStates.TryGetValue(channelId, out var lastUpdate))
        {
            return false;
        }
        
        // Console.WriteLine($"[{channelId}]: {lastRead} < {lastUpdate.LastUpdateTime}");
        
        return lastRead < lastUpdate.LastUpdateTime;
    }

    public static async Task<TaskResult<List<ReferralDataModel>>> GetReferralsAsync()
    {
        return await PrimaryNode.GetJsonAsync<List<ReferralDataModel>>("api/users/self/referrals");
    }

    /// <summary>
    /// Tries to add the given Tenor favorite
    /// </summary>
    public static async Task<TaskResult<TenorFavorite>> AddTenorFavorite(TenorFavorite favorite)
    {
        var result = await TenorFavorite.PostAsync(favorite);

        if (result.Success)
            TenorFavorites.Add(result.Data);

        return result;
    }

    /// <summary>
    /// Tries to delete the given Tenor favorite
    /// </summary>
    public static async Task<TaskResult> RemoveTenorFavorite(TenorFavorite favorite)
    {
        var result = await TenorFavorite.DeleteAsync(favorite);

        if (result.Success)
            TenorFavorites.RemoveAll(x => x.Id == favorite.Id);

        return result;
    }

    public static async Task HandleFriendEventReceived(FriendEventData eventData)
    {
        if (eventData.Type == FriendEventType.Added)
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
        else if (eventData.Type == FriendEventType.Removed)
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
        
        if (OnFriendEvent is not null)
            await OnFriendEvent.Invoke(eventData);
    }

    /// <summary>
    /// Adds a friend
    /// </summary>
    public static async Task<TaskResult<UserFriend>> AddFriendAsync(string nameAndTag)
    {
        var result = await PrimaryNode.PostAsyncWithResponse<UserFriend>($"api/userfriends/add/{HttpUtility.UrlEncode(nameAndTag)}");

        if (result.Success)
        {
            var addedUser = await User.FindAsync(result.Data.FriendId);

			// If we already had a friend request from them,
			// add them to the friends list
			var request = FriendRequests.FirstOrDefault(x => x.NameAndTag.ToLower() == nameAndTag.ToLower());
            if (request is not null)
            {
                FriendRequests.Remove(request);
				Friends.Add(addedUser);
                FriendFastLookup.Add(addedUser.Id);

                if (OnFriendsUpdate is not null)
                    await OnFriendsUpdate.Invoke();
			}
			// Otherwise, add this request to our request list
			else
			{
                FriendsRequested.Add(addedUser);
            }
		}

        return result;
    }

	/// <summary>
	/// Declines a friend request
	/// </summary>
	public static async Task<TaskResult> DeclineFriendAsync(string nameAndTag)
	{
		var result = await PrimaryNode.PostAsync($"api/userfriends/decline/{HttpUtility.UrlEncode(nameAndTag)}", null);

        if (result.Success)
        {
            var declined = FriendRequests.FirstOrDefault(x => x.NameAndTag.ToLower() == nameAndTag.ToLower());
            if (declined is not null)
                FriendRequests.Remove(declined);
        }

		return result;
	}

	/// <summary>
	/// Removes a friend
	/// </summary>
	public static async Task<TaskResult> RemoveFriendAsync(string nameAndTag)
    {
        var result = await PrimaryNode.PostAsync($"api/userfriends/remove/{HttpUtility.UrlEncode(nameAndTag)}", null);

        if (result.Success)
        {
            FriendsRequested.RemoveAll(x => x.NameAndTag.ToLower() == nameAndTag.ToLower());
            var friend = Friends.FirstOrDefault(x => x.NameAndTag.ToLower() == nameAndTag.ToLower());
            if (friend is not null)
            {
                Friends.Remove(friend);
                FriendFastLookup.Remove(friend.Id);

                FriendRequests.Add(friend);

                if (OnFriendsUpdate is not null)
					await OnFriendsUpdate.Invoke();
			}
        }
        return result;
    }

	/// <summary>
	/// Cancels a friend request
	/// </summary>
	public static async Task<TaskResult> CancelFriendAsync(string nameAndTag)
	{
		var result = await PrimaryNode.PostAsync($"api/userfriends/cancel/{HttpUtility.UrlEncode(nameAndTag)}", null);

		if (result.Success)
		{
			var canceled = FriendsRequested.FirstOrDefault(x => x.NameAndTag.ToLower() == nameAndTag.ToLower());
			if (canceled is not null)
				FriendsRequested.Remove(canceled);
		}

		return result;
	}

	#region SignalR Groups

	/// <summary>
	/// Returns if the given planet is open
	/// </summary>
	public static bool IsPlanetOpen(Planet planet) =>
        OpenPlanets.Any(x => x.Id == planet.Id);

    /// <summary>
    /// Returns if the channel is open
    /// </summary>
    public static bool IsPlanetChannelOpen(Channel channel) =>
        OpenPlanetChannels.Any(x => x.Id == channel.Id);

    /// <summary>
    /// Opens a planet and prepares it for use
    /// </summary>
    public static async Task OpenPlanetConnection(Planet planet, string key)
    {
        // Cannot open null
        if (planet == null)
            return;

        if (PlanetLocks.ContainsKey(key))
        {
            PlanetLocks[key] = planet.Id;
        }
        else
        {
            // Add lock
            AddPlanetLock(key, planet.Id);
        }

        // Already open
        if (OpenPlanets.Contains(planet))
            return;

        // Mark as opened
        OpenPlanets.Add(planet);

        Console.WriteLine($"Opening planet {planet.Name} ({planet.Id})");

        Stopwatch sw = new Stopwatch();

        sw.Start();

        // Get node for planet
        var node = await NodeManager.GetNodeForPlanetAsync(planet.Id);

        List<Task> tasks = new();

        // Joins SignalR group
        var result = await node.HubConnection.InvokeAsync<TaskResult>("JoinPlanet", planet.Id);
        Console.WriteLine(result.Message);

        if (!result.Success)
            return;

        // Load roles early for cached speed
        await planet.LoadRolesAsync();

        // Load member data early for the same reason (speed)
        tasks.Add(planet.LoadMemberDataAsync());

        // Load channels
        tasks.Add(planet.LoadChannelsAsync());
        
        // Load permissions nodes
        tasks.Add(planet.LoadPermissionsNodesAsync());

        // requesting/loading the data does not require data from other requests/types
        // so just await them all, instead of one by one
        await Task.WhenAll(tasks);

        sw.Stop();

        Console.WriteLine($"Time to open this Planet: {sw.ElapsedMilliseconds}ms");

        // Log success
        Console.WriteLine($"Joined SignalR group for planet {planet.Name} ({planet.Id})");

        if (OnPlanetOpen is not null)
        {
            Console.WriteLine($"Invoking Open Planet event for {planet.Name} ({planet.Id})");
            await OnPlanetOpen.Invoke(planet);
        }
    }

    /// <summary>
    /// Prevents a planet from closing connections automatically.
    /// Key is used to allow multiple locks per planet.
    /// </summary>
    private static void AddPlanetLock(string key, long planetId)
    {
        PlanetLocks[key] = planetId;
    }

    /// <summary>
    /// Removes the lock for a planet.
    /// Returns if there are any locks left for the planet.
    /// </summary>
    private static bool RemovePlanetLock(string key)
    {
        var found = PlanetLocks.TryGetValue(key, out var planetId);

        if (found)
        {
            PlanetLocks.Remove(key);
        }

        return !PlanetLocks.Any(x => x.Value == planetId);
    }
    
    /// <summary>
    /// Prevents a channel from closing connections automatically.
    /// Key is used to allow multiple locks per channel.
    /// </summary>
    private static void AddChannelLock(string key, long planetId)
    {
        ChannelLocks[key] = planetId;
    }

    /// <summary>
    /// Removes the lock for a channel.
    /// Returns if there are any locks left for the channel.
    /// </summary>
    private static bool RemoveChannelLock(string key)
    {
        var found = ChannelLocks.TryGetValue(key, out var channelId);

        if (!found)
        {
            ChannelLocks.Remove(key);
        }

        return !ChannelLocks.Any(x => x.Value == channelId);
    }

    /// <summary>
    /// Closes a SignalR connection to a planet
    /// </summary>
    public static async Task ClosePlanetConnection(Planet planet, string key, bool force = false)
    {
        if (!force)
        {
            var locked = RemovePlanetLock(key);
            if (locked)
                return;
        }

        // Already closed
        if (!OpenPlanets.Contains(planet))
            return;

        // Close connection
        await planet.Node.HubConnection.SendAsync("LeavePlanet", planet.Id);

        // Remove from list
        OpenPlanets.Remove(planet);

        Console.WriteLine($"Left SignalR group for planet {planet.Name} ({planet.Id})");

        // Invoke event
        if (OnPlanetClose is not null)
        {
            Console.WriteLine($"Invoking close planet event for {planet.Name} ({planet.Id})");
            await OnPlanetClose.Invoke(planet);
        }
    }

    /// <summary>
    /// Opens a SignalR connection to a channel if it does not already have one,
    /// and stores a key to prevent it from being closed
    /// </summary>
    public static async Task OpenPlanetChannelConnection(Channel channel, string key)
    {
        if (channel.ChannelType != ChannelTypeEnum.PlanetChat)
            return;

        if (ChannelLocks.ContainsKey(key))
        {
            ChannelLocks[key] = channel.Id;
        }
        else
        {
            // Add lock
            AddChannelLock(key, channel.Id);   
        }
        
        // Already opened
        if (OpenPlanetChannels.Contains(channel))
            return;

        var planet = await channel.GetPlanetAsync();

        // Ensure planet is opened
        await OpenPlanetConnection(planet, key);

        // Join channel SignalR group
        var result = await channel.Node.HubConnection.InvokeAsync<TaskResult>("JoinChannel", channel.Id);
        Console.WriteLine(result.Message);

        if (!result.Success)
            return;

        // Add to open set
        OpenPlanetChannels.Add(channel);

        Console.WriteLine($"Joined SignalR group for channel {channel.Name} ({channel.Id})");

        if (OnChannelOpen is not null)
            await OnChannelOpen.Invoke(channel);
    }

    /// <summary>
    /// Closes a SignalR connection to a channel
    /// </summary>
    public static async Task ClosePlanetChannelConnection(Channel channel, string key, bool force = false)
    {
        if (channel.ChannelType != ChannelTypeEnum.PlanetChat)
            return;

        if (!force)
        {
            // Remove key from locks
            var locked = RemoveChannelLock(key);

            // If there are still any locks, don't close
            if (locked)
                return;
        }

        // Not opened
        if (!OpenPlanetChannels.Contains(channel))
            return;

        // Leaves channel SignalR group
        await channel.Node.HubConnection.SendAsync("LeaveChannel", channel.Id);

        // Remove from open set
        OpenPlanetChannels.Remove(channel);

        Console.WriteLine($"Left SignalR group for channel {channel.Name} ({channel.Id})");

        if (OnChannelClose is not null)
            await OnChannelClose.Invoke(channel);

        await ClosePlanetConnection(await channel.GetPlanetAsync(), key);
    }
    
    /// <summary>
    /// Subscribe to Valour Plus! (...or Premium? What are we even calling it???)
    /// </summary>
    public static async Task<TaskResult> SubscribeAsync(string type)
    {
        var result = await PostAsyncWithResponse<TaskResult>($"api/subscriptions/{type}/start");
        if (!result.Success)
        {
            return new TaskResult(false, result.Message);
        }

        return result.Data;
    }
    
    /// <summary>
    /// Unsubscribe (sobs quietly in the corner)
    /// </summary>
    public static async Task<TaskResult> UnsubscribeAsync()
    {
        var result = await PostAsyncWithResponse<TaskResult>($"api/subscriptions/end");
        if (!result.Success)
        {
            return new TaskResult(false, result.Message);
        }

        return result.Data;
    }
    
    public static async Task<decimal> GetSubscriptionPriceAsync(string type)
    {
        var result = await GetJsonAsync<decimal>($"api/subscriptions/{type}/price");
        return result.Data;
    }

    public static async Task<UserSubscription> GetActiveSubscriptionAsync()
    {
        var result = await GetJsonAsync<UserSubscription>($"api/subscriptions/active/{Self.Id}", true);
        return result.Data;
    }

    #endregion

    #region SignalR Events

    public static async Task HandleRefocus()
    {
        foreach (var node in NodeManager.Nodes)
        {
            await node.ForceRefresh();
        }
        
        if (OnRefocus is not null)
            await OnRefocus.Invoke();
    }

    public static async Task NotifyNodeReconnect(Node node)
    {
        if (OnNodeReconnect is not null)
            await OnNodeReconnect.Invoke(node);
    }

    public static async Task HandleUpdateChannelState(ChannelStateUpdate update)
    {
        // Right now only planet chat channels have state updates
        var channel = ValourCache.Get<Channel>(update.ChannelId);
        if (channel is null)
            return;
        
        if (!CurrentChannelStates.TryGetValue(channel.Id, out var state))
        {
            state = new ChannelState()
            {
                ChannelId = update.ChannelId,
                PlanetId = update.PlanetId,
                LastUpdateTime = update.Time
            };

            CurrentChannelStates[channel.Id] = state;
        }
        else
        {
            CurrentChannelStates[channel.Id].LastUpdateTime = update.Time;
        }

        if (OnChannelStateUpdate is not null)
            await OnChannelStateUpdate.Invoke(update);
    }

    /// <summary>
    /// Updates an item's properties
    /// </summary>
    public static async Task UpdateItem<T>(T updated, int flags, bool skipEvent = false) where T : LiveModel
    {
        // printing to console is SLOW, only turn on for debugging reasons
        //Console.WriteLine("Update for " + updated.Id + ",  skipEvent is " + skipEvent);

        // Create object for event data
        var eventData = new ModelUpdateEvent<T>();
        eventData.Flags = flags;

        var local = ValourCache.Get<T>(updated.Id);

        if (local != null)
        {
            // Find changed properties
            var pInfo = local.GetType().GetProperties();

            foreach (var prop in pInfo)
            {
                var a = prop.GetValue(local);
                var b = prop.GetValue(updated);

                if (a != b)
                    eventData.PropsChanged.Add(prop.Name);
            }
            
            // Update local copy
            updated.CopyAllTo(local);
        }
        else
        {
            // Set new flag
            eventData.NewToClient = true;
        }

        if (!skipEvent)
        {
            // Update
            if (local != null)
            {
                eventData.Model = local;
                // Fire off local event on item
                await local.InvokeUpdatedEventAsync(new ModelUpdateEvent(){ Flags = flags, PropsChanged = eventData.PropsChanged});
            }
            // New
            else
            {
                eventData.Model = updated;
                await updated.AddToCache(updated);
            }
            
            // Fire off global events
            await ModelObserver<T>.InvokeAnyUpdated(eventData);

            // printing to console is SLOW, only turn on for debugging reasons
            //Console.WriteLine("Invoked update events for " + updated.Id);
        }
    }

    /// <summary>
    /// Updates an item's properties
    /// </summary>
    public static async Task DeleteItem<T>(T item) where T : LiveModel
    {
        var local = ValourCache.Get<T>(item.Id);
        
        ValourCache.Remove<T>(item.Id);

        if (local is null)
        {
            // Invoke static "any" delete
            await item.InvokeDeletedEventAsync();
            await ModelObserver<T>.InvokeAnyDeleted(item);
        }
        else
        {
            // Invoke static "any" delete
            await local.InvokeDeletedEventAsync();
            await ModelObserver<T>.InvokeAnyDeleted(local);
        }
    }

    public static int GetPlanetNotifications(long planetId)
    {
        return UnreadNotifications.Count(x => x.PlanetId == planetId);
    }

    public static int GetChannelNotifications(long channelId)
    {
        return UnreadNotifications.Count(x => x.ChannelId == channelId);
    }

    public static async Task HandleNotificationReceived(Notification notification)
    {
        await notification.AddToCache(notification);
        var cached = ValourCache.Get<Notification>(notification.Id);
        
        if (cached.TimeRead is null)
        {
            if (!UnreadNotifications.Contains(cached))
                UnreadNotifications.Add(cached);

            if (cached.SourceId != null)
            {
                UnreadNotificationsLookup[cached.SourceId.Value] = cached;
            }
        }
        else
        {
            UnreadNotifications.RemoveAll(x => x.Id == cached.Id);
            if (cached.SourceId != null)
            {
                UnreadNotificationsLookup.Remove(cached.SourceId.Value);
            }
        }

        if (OnNotificationReceived is not null)
            await OnNotificationReceived.Invoke(cached);
    }

    public static async Task HandleNotificationsCleared()
    {
        UnreadNotifications.Clear();
        UnreadNotificationsLookup.Clear();
        
        if (OnNotificationsCleared is not null)
            await OnNotificationsCleared.Invoke();
    }

    /// <summary>
    /// Ran when a message is recieved
    /// </summary>
    public static async Task HandlePlanetMessageReceived(Message message)
    {
        Console.WriteLine($"[{message.Node?.Name}]: Received planet message {message.Id} for channel {message.ChannelId}");
        await ValourCache.Put(message.Id, message);
        if (message.ReplyTo is not null)
        {
            await ValourCache.Put(message.ReplyTo.Id, message.ReplyTo);
        }

        if (OnMessageReceived is not null)
            await OnMessageReceived.Invoke(message);
    }
    
    /// <summary>
    /// Ran when a message is edited
    /// </summary>
    public static async Task HandlePlanetMessageEdited(Message message)
    {
        Console.WriteLine($"[{message.Node?.Name}]: Received planet message edit {message.Id} for channel {message.ChannelId}");
        await ValourCache.Put(message.Id, message);
        if (message.ReplyTo is not null)
        {
            await ValourCache.Put(message.ReplyTo.Id, message.ReplyTo);
        }
        
        if (OnMessageEdited is not null)
            await OnMessageEdited.Invoke(message);
    }
    
    /// <summary>
    /// Ran when a message is recieved
    /// </summary>
    public static async Task HandleDirectMessageReceived(Message message)
    {
        Console.WriteLine($"[{message.Node?.Name}]: Received direct message {message.Id} for channel {message.ChannelId}");
        await ValourCache.Put(message.Id, message);
        if (message.ReplyTo is not null)
        {
            await ValourCache.Put(message.ReplyTo.Id, message.ReplyTo);
        }
        
        if (OnMessageReceived is not null)
            await OnMessageReceived.Invoke(message);
    }
    
    /// <summary>
    /// Ran when a message is edited
    /// </summary>
    public static async Task HandleDirectMessageEdited(Message message)
    {
        Console.WriteLine($"[{message.Node?.Name}]: Received direct message edit {message.Id} for channel {message.ChannelId}");
        await ValourCache.Put(message.Id, message);
        if (message.ReplyTo is not null)
        {
            await ValourCache.Put(message.ReplyTo.Id, message.ReplyTo);
        }
        
        if (OnMessageEdited is not null)
            await OnMessageEdited.Invoke(message);
    }

    public static async Task HandleMessageDeleted(Message message)
    {
        if (OnMessageDeleted is not null)
            await OnMessageDeleted.Invoke(message);
    }

    public static async Task HandleChannelWatchingUpdateRecieved(ChannelWatchingUpdate update)
    {
        //Console.WriteLine("Watching: " + update.ChannelId);
        //foreach (var watcher in update.UserIds)
        //{
        //    Console.WriteLine("- " + watcher);
        //}

        if (OnChannelWatchingUpdate is not null)
            await OnChannelWatchingUpdate.Invoke(update);
    }

    public static async Task HandleChannelCurrentlyTypingUpdateRecieved(ChannelTypingUpdate update)
    {
        if (OnChannelCurrentlyTypingUpdate is not null)
            await OnChannelCurrentlyTypingUpdate.Invoke(update);
    }

    public static async Task HandlePersonalEmbedUpdate(PersonalEmbedUpdate update)
    {
        if (OnPersonalEmbedUpdate is not null)
            await OnPersonalEmbedUpdate.Invoke(update);
    }

    public static async Task HandleChannelEmbedUpdate(ChannelEmbedUpdate update)
    {
        if (OnChannelEmbedUpdate is not null)
            await OnChannelEmbedUpdate.Invoke(update);
    }

    public static async Task HandleCategoryOrderUpdate(CategoryOrderEvent eventData)
    {
        // Update channels in cache
        int pos = 0;
        foreach (var data in eventData.Order)
        {
            var channel = ValourCache.Get<Channel>(data.Id);
            if (channel is not null)
            {
                Console.WriteLine($"{pos}: {channel.Name}");

                // The parent can be changed in this event
                channel.ParentId = eventData.CategoryId;

                // Position can be changed in this event
                channel.Position = pos;
            }

            pos++;
        }
        
        if (OnCategoryOrderUpdate is not null)
            await OnCategoryOrderUpdate.Invoke(eventData);
    }

    #endregion

    #region Planet Event Handling

        private static void HookPlanetEvents()
        {
            ModelObserver<PlanetRoleMember>.OnAnyUpdated += OnMemberRoleAdded;
            ModelObserver<PlanetRoleMember>.OnAnyDeleted += OnMemberRoleDeleted;
            ModelObserver<PlanetRole>.OnAnyUpdated += OnPlanetRoleUpdated;
            ModelObserver<PlanetRole>.OnAnyDeleted += OnPlanetRoleDeleted;

            ModelObserver<Planet>.OnAnyDeleted += OnPlanetDeleted;
        }

        private static async Task OnPlanetDeleted(Planet planet)
        {
            _joinedPlanetIds.Remove(planet.Id);
            JoinedPlanets = JoinedPlanets.Where(x => x.Id != planet.Id).ToList();
            await ClosePlanetConnection(planet, "", true);
        }
        
        private static async Task OnPlanetRoleUpdated(ModelUpdateEvent<PlanetRole> eventData)
        {
            // If we don't have the planet loaded, we don't need to update anything
            var planet = ValourCache.Get<Planet>(eventData.Model.PlanetId);
            if (planet is null)
                return;

            // This is a little messy.
            foreach (var member in await planet.GetMembersAsync())
            {
                // Skip if doesn't have role
                if (!(await member.GetRolesAsync()).Contains(eventData.Model))
                    continue;
                
                // If it does have the role, fire event
                await member.NotifyRoleEventAsync(new MemberRoleEvent()
                {
                    Type = MemberRoleEventType.Added,
                    Role = eventData.Model,
                });
            }
        }
        
        private static async Task OnPlanetRoleDeleted(PlanetRole role)
        {
            // If we don't have the planet loaded, we don't need to update anything
            var planet = ValourCache.Get<Planet>(role.PlanetId);
            if (planet is null)
                return;

            // This is a little messy.
            foreach (var member in await planet.GetMembersAsync())
            {
                // Skip if doesn't have role
                if (!(await member.GetRolesAsync()).Contains(role))
                    continue;
                
                // If it does have the role, fire event
                await member.NotifyRoleEventAsync(new MemberRoleEvent()
                {
                    // Deleting the role also means removing it from everyone
                    Type = MemberRoleEventType.Removed,
                    Role = role,
                });
            }
        }

        private static async Task OnMemberRoleAdded(ModelUpdateEvent<PlanetRoleMember> eventData)
        {
            // If we don't have the member loaded, we don't need to update anything
            var member = ValourCache.Get<PlanetMember>(eventData.Model.MemberId);
            if (member is null)
                return;

            var role = await PlanetRole.FindAsync(eventData.Model.RoleId, eventData.Model.PlanetId);
            await member.NotifyRoleEventAsync(new MemberRoleEvent()
            {
                Type = MemberRoleEventType.Added,
                Role = role,
            });
        }

        private static async Task OnMemberRoleDeleted(PlanetRoleMember roleMember)
        {
            // If we don't have the member loaded, we don't need to update anything
            var member = ValourCache.Get<PlanetMember>(roleMember.MemberId);
            if (member is null)
                return;

            var role = await PlanetRole.FindAsync(roleMember.RoleId, roleMember.PlanetId);
            await member.NotifyRoleEventAsync(new MemberRoleEvent()
            {
                Type = MemberRoleEventType.Removed,
                Role = role,
            });
        }

    #endregion

    #region Initialization

    /// <summary>
    /// Gets the Token for the client
    /// </summary>
    public static async Task<TaskResult<AuthToken>> GetToken(string email, string password)
    {
        TokenRequest request = new()
        {
            Email = email,
            Password = password
        };

        var response = await PostAsyncWithResponse<AuthToken>($"api/users/token", request);

        if (response.Success)
        {
            var token = response.Data.Id;
            _token = token;
        }
        
        return response;
    }

    /// <summary>
    /// Logs in and prepares the client for use
    /// </summary>
    public static async Task<TaskResult<User>> InitializeUser(string token)
    {
        // Store token 
        _token = token;

        if (Http.DefaultRequestHeaders.Contains("authorization"))
        {
            Http.DefaultRequestHeaders.Remove("authorization");
        }

        // Add auth header so we never have to do that again
        Http.DefaultRequestHeaders.Add("authorization", Token);

        TaskResult<string> nodeName;

        do
        {
            // Get primary node identity
            nodeName = await GetAsync("api/node/name");

            if (!nodeName.Success)
            {
                Console.WriteLine("Failed to get primary node name... trying again in three seconds.");
                Console.WriteLine("(Possible network issues)");
                await Task.Delay(3000);
            }
            
        } while (!nodeName.Success);
            
        // Set node to primary node for main http client
        Http.DefaultRequestHeaders.Add("X-Server-Select", nodeName.Data);
        
        // Get user

        var response = await GetJsonAsync<User>($"api/users/self");

        if (!response.Success)
            return response;

        // Set reference to self user
        Self = response.Data;

        // Now that we have our user, it should have node data we can use to set up our first node
        // Initialize primary node
        PrimaryNode = new Node();
        await PrimaryNode.InitializeAsync(nodeName.Data, _token, true);

        var loadTasks = new List<Task>()
        {
            // LoadChannelStatesAsync(), this is already done by the Home component
            LoadFriendsAsync(),
            LoadJoinedPlanetsAsync(),
            LoadTenorFavoritesAsync(),
            LoadDirectChatChannelsAsync(),
            LoadUnreadNotificationsAsync(),
        };

        // Load user data concurrently
        await Task.WhenAll(loadTasks);

        if (OnLogin is not null)
            await OnLogin.Invoke();
        
        return new TaskResult<User>(true, "Success", Self);
    }

    /// <summary>
    /// Logs in and prepares the bot's client for use
    /// </summary>
    public static async Task<TaskResult<User>> InitializeBot(string email, string password, HttpClient http = null)
    {
        SetHttpClient(http is not null ? http : new HttpClient()
        {
            BaseAddress = new Uri(BaseAddress)
        });

        var tokenResult = await GetToken(email, password);

        if (!tokenResult.Success) 
            return new TaskResult<User>(false, tokenResult.Message);
        
        TaskResult<string> nodeName;

        do
        {
            // Get primary node identity
            nodeName = await GetAsync("api/node/name");

            if (!nodeName.Success)
            {
                Console.WriteLine("Failed to get primary node name... trying again in three seconds.");
                Console.WriteLine("(Possible network issues)");
                await Task.Delay(3000);
            }
            
        } while (!nodeName.Success);
        
        // Set node to primary node for main http client
        Http.DefaultRequestHeaders.Add("X-Server-Select", nodeName.Data);

        // Get user

        var response = await GetJsonAsync<User>($"api/users/self");

        if (!response.Success)
            return response;

        // Set reference to self user
        Self = response.Data;

        // Now that we have our user, it should have node data we can use to set up our first node
        // Initialize primary node
        PrimaryNode = new Node();
        await PrimaryNode.InitializeAsync(nodeName.Data, _token, true);

        // Add auth header so we never have to do that again
        Http.DefaultRequestHeaders.Add("authorization", Token);

        Console.WriteLine($"Initialized bot {Self.Name} ({Self.Id})");

        if (OnLogin is not null)
            await OnLogin.Invoke();

        await JoinAllChannelsAsync();

        return new TaskResult<User>(true, "Success", Self);
    }

    /// <summary>
    /// Should only be run during initialization of bots!
    /// </summary>
    public static async Task JoinAllChannelsAsync()
    {
        // Get all joined planets
        var planets = (await PrimaryNode.GetJsonAsync<List<Planet>>("api/users/self/planets")).Data;

        var planetTasks = new List<Task>();
        
        // Add to cache
        foreach (var planet in planets)
        {
            planetTasks.Add(new Task(async () =>
            {
                await ValourCache.Put(planet.Id, planet);

                await OpenPlanetConnection(planet, "bot-init");

                var channels = await planet.GetChatChannelsAsync();

                foreach (var channel in channels)
                {
                    await OpenPlanetChannelConnection(channel, "bot-init");
                }
            }));
        }

        await Task.WhenAll(planetTasks);

        JoinedPlanets = planets;

        _joinedPlanetIds = JoinedPlanets.Select(x => x.Id).ToList();

        if (OnJoinedPlanetsUpdate != null)
            await OnJoinedPlanetsUpdate.Invoke();
    }

    public static async Task HandleUpdateUserChannelState(UserChannelState channelState)
    {
        ChannelsLastViewedState[channelState.ChannelId] = channelState.LastViewedTime;
        
        // Access dict again to maintain references (do not try to optimize and break everything)
        if (OnUserChannelStateUpdate is not null)
            await OnUserChannelStateUpdate.Invoke(channelState);
    }

    public static async Task LoadTenorFavoritesAsync()
    {
        var response = await PrimaryNode.GetJsonAsync<List<TenorFavorite>>("api/users/self/tenorfavorites");
        if (!response.Success)
        {
            await Logger.Log("** Failed to load Tenor favorites **", "red");
            await Logger.Log(response.Message, "red");

            return;
        }

        TenorFavorites = response.Data;
        
        Console.WriteLine($"Loaded {TenorFavorites.Count} Tenor favorites");
    }

    public static async Task LoadDirectChatChannelsAsync()
    {
        var response = await PrimaryNode.GetJsonAsync<List<Channel>>("api/channels/direct/self");
        if (!response.Success)
        {
            Console.WriteLine("** Failed to load direct chat channels **");
            Console.WriteLine(response.Message);

            return;
        }
        
        foreach (var channel in response.Data)
        {
            // Custom cache insert behavior
            if (channel.Members is not null && channel.Members.Count > 0)
            {
                var id0 = channel.Members[0].Id;
                
                // Self channel
                if (channel.Members.Count == 1)
                {
                    Channel.DirectChannelIdLookup.Add((id0, id0), channel.Id);
                }
                // Other channel
                else if (channel.Members.Count == 2)
                {
                    var id1 = channel.Members[1].Id;
                    
                    if (id0 > id1)
                    {
                        // Swap
                        (id0, id1) = (id1, id0);
                    }
                    
                    Channel.DirectChannelIdLookup.Add((id0, id1), channel.Id);
                }
            }
            
            await channel.AddToCache(channel);
        }

        if (DirectChatChannels is null)
            DirectChatChannels = new();
        else
            DirectChatChannels.Clear();

        // This second step is necessary because we need to ensure we only use the cache objects
        foreach (var channel in response.Data)
        {
            DirectChatChannels.Add(ValourCache.Get<Channel>(channel.Id));
        }
        
        Console.WriteLine($"Loaded {DirectChatChannels.Count} direct chat channels...");
    }
    
    public static async Task LoadChannelStatesAsync()
    {
        var response = await PrimaryNode.GetJsonAsync<List<ChannelStateData>>($"api/users/self/statedata");
        if (!response.Success)
        {
            Console.WriteLine("** Failed to load channel states **");
            Console.WriteLine(response.Message);

            return;
        }

        foreach (var state in response.Data)
        {
            if (state.ChannelState is not null)
                CurrentChannelStates[state.ChannelId] = state.ChannelState;
            
            if (state.LastViewedTime is not null)
                ChannelsLastViewedState[state.ChannelId] = state.LastViewedTime;
        }

        Console.WriteLine("Loaded " + ChannelsLastViewedState.Count + " channel states.");
        // Console.WriteLine(JsonSerializer.Serialize(response.Data));
    }

    /// <summary>
    /// Should only be run during initialization!
    /// </summary>
    public static async Task LoadJoinedPlanetsAsync()
    {
        var response = await PrimaryNode.GetJsonAsync<List<Planet>>($"api/users/self/planets");

        if (!response.Success)
            return;

        var planets = response.Data;

        // Add to cache
        foreach (var planet in planets)
            await planet.AddToCache(planet);

        JoinedPlanets = planets;

        _joinedPlanetIds = JoinedPlanets.Select(x => x.Id).ToList();

        if (OnJoinedPlanetsUpdate is not null)
            await OnJoinedPlanetsUpdate.Invoke();
    }

    public static async Task LoadUnreadNotificationsAsync()
    {
        var response = await PrimaryNode.GetJsonAsync<List<Notification>>($"api/notifications/self/unread/all");

        if (!response.Success)
            return;

        var notifications = response.Data;

        // Add to cache
        foreach (var notification in notifications)
            await notification.AddToCache(notification);
        
        UnreadNotifications.Clear();
        UnreadNotificationsLookup.Clear();
        
        foreach (var notification in notifications)
        {
            var cached = ValourCache.Get<Notification>(notification.Id);
            if (cached is null)
                continue;

            // Only add if unread
            if (notification.TimeRead is not null)
                continue;
            
            if (!UnreadNotifications.Contains(cached))
                UnreadNotifications.Add(cached);
            
            if (cached.SourceId is not null)
                UnreadNotificationsLookup[cached.SourceId.Value] = cached;
        }
    }

    public static async Task<TaskResult> MarkNotificationRead(Notification notification, bool value)
    {
        var result = await PrimaryNode.PostAsync($"api/notifications/self/{notification.Id}/read/{value}", null);
        return result;
    }

    public static async Task<TaskResult> ClearNotificationsAsync()
    {
        var result = await PrimaryNode.PostAsync("api/notifications/self/clear", null);
        return result;
    }

    public static async Task LoadFriendsAsync()
    {
        var friendResult = await Self.GetFriendDataAsync();

        if (!friendResult.Success)
        {
            await Logger.Log("Error loading friends.", "red");
            await Logger.Log(friendResult.Message, "red");
            return;
        }

        var data = friendResult.Data;

        foreach (var added in data.Added)
            await ValourCache.Put(added.Id, added);

        foreach (var addedBy in data.AddedBy)
            await ValourCache.Put(addedBy.Id, addedBy);

        Friends = new();
        FriendFastLookup = new();
        FriendRequests = data.AddedBy;
        FriendsRequested = data.Added;

        foreach (var req in FriendRequests)
        {
            if (FriendsRequested.Any(x => x.Id == req.Id))
            {
                Friends.Add(req);
                FriendFastLookup.Add(req.Id);
            }
        }

        foreach (var friend in Friends)
        {
            FriendRequests.RemoveAll(x => x.Id == friend.Id);
            FriendsRequested.RemoveAll(x => x.Id == friend.Id);
        }

        await Logger.Log($"Loaded {Friends.Count} friends.", "cyan");
    }

    public static async Task<List<Planet>> GetDiscoverablePlanetsAsync()
    {
        var response = await PrimaryNode.GetJsonAsync<List<Planet>>($"api/planets/discoverable");
        if (!response.Success)
            return new List<Planet>();

        var planets = response.Data;

        foreach (var planet in planets)
            await ValourCache.Put(planet.Id, planet, true);

        return planets;
    }

    /// <summary>
    /// Refreshes the user's joined planet list from the server
    /// </summary>
    public static async Task RefreshJoinedPlanetsAsync()
    {
        var response = await PrimaryNode.GetJsonAsync<List<long>>($"api/users/self/planetIds");

        if (!response.Success)
            return;

        var planetIds = response.Data;

        JoinedPlanets.Clear();

        foreach (var id in planetIds)
        {
            JoinedPlanets.Add(await Planet.FindAsync(id));
        }

        if (OnJoinedPlanetsUpdate is not null)
            await OnJoinedPlanetsUpdate.Invoke();
    }

    #endregion

    #region HTTP Helpers

    /// <summary>
    /// Gets a json resource from the given uri and deserializes it
    /// </summary>
    public static async Task<TaskResult<T>> GetJsonAsync<T>(string uri, bool allowNull = false, HttpClient http = null)
    {
        if (http is null)
            http = Http;

        var response = await http.GetAsync(BaseAddress + uri, HttpCompletionOption.ResponseHeadersRead);

        TaskResult<T> result = new()
        {
            Success = response.IsSuccessStatusCode,
            Data = default(T),
            Code = (int)response.StatusCode
        };

        if (!response.IsSuccessStatusCode)
        {
            result.Message = await response.Content.ReadAsStringAsync();

            // This means the null is expected
            if (allowNull && response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return TaskResult<T>.FromData(default(T));
            }

            Console.WriteLine("-----------------------------------------\n" +
                              "Failed GET response for the following:\n" +
                              $"[{uri}]\n" +
                              $"Code: {response.StatusCode}\n" +
                              $"Message: {result.Message}\n" +
                              $"-----------------------------------------");

            Console.WriteLine(Environment.StackTrace);
        }
        else
        {
            result.Message = "Success";
            result.Data = await JsonSerializer.DeserializeAsync<T>(await response.Content.ReadAsStreamAsync(), DefaultJsonOptions);
        }

        return result;
    }

    /// <summary>
    /// Gets a json resource from the given uri and deserializes it
    /// </summary>
    public static async Task<TaskResult<string>> GetAsync(string uri, HttpClient http = null)
    {
        if (http is null)
            http = Http;

        var response = await http.GetAsync(BaseAddress + uri, HttpCompletionOption.ResponseHeadersRead);
        var msg = await response.Content.ReadAsStringAsync();

        TaskResult<string> result = new()
        {
            Success = response.IsSuccessStatusCode,
            Code = (int)response.StatusCode
        };

        if (!response.IsSuccessStatusCode)
        {
            result.Message = msg;

            Console.WriteLine("-----------------------------------------\n" +
                              "Failed GET response for the following:\n" +
                              $"[{uri}]\n" +
                              $"Code: {response.StatusCode}\n" +
                              $"Message: {msg}\n" +
                              $"-----------------------------------------");

            Console.WriteLine(Environment.StackTrace);

            return result;
        }
        else
        {
            result.Message = "Success";
            result.Data = msg;
            return result;
        }
    }

    /// <summary>
    /// Puts a string resource in the specified uri and returns the response message
    /// </summary>
    public static async Task<TaskResult> PutAsync(string uri, string content, HttpClient http = null)
    {
        if (http is null)
            http = Http;

        StringContent stringContent = new StringContent(content);

        var response = await http.PutAsync(BaseAddress + uri, stringContent);
        var msg = await response.Content.ReadAsStringAsync();

        TaskResult result = new()
        {
            Success = response.IsSuccessStatusCode,
            Message = msg,
            Code = (int)response.StatusCode
        };

        if (!result.Success)
        {
            Console.WriteLine("-----------------------------------------\n" +
                              "Failed PUT response for the following:\n" +
                              $"[{uri}]\n" +
                              $"Code: {response.StatusCode}\n" +
                              $"Message: {msg}\n" +
                              $"-----------------------------------------");

            Console.WriteLine(Environment.StackTrace);
        }

        return result;
    }

    /// <summary>
    /// Puts a json resource in the specified uri and returns the response message
    /// </summary>
    public static async Task<TaskResult> PutAsync(string uri, object content, HttpClient http = null)
    {
        if (http is null)
            http = Http;

        JsonContent jsonContent = JsonContent.Create(content);

        var response = await http.PutAsync(BaseAddress + uri, jsonContent);
        var msg = await response.Content.ReadAsStringAsync();

        TaskResult result = new()
        {
            Success = response.IsSuccessStatusCode,
            Message = msg,
            Code = (int)response.StatusCode
        };

        if (!result.Success)
        {
            Console.WriteLine("-----------------------------------------\n" +
                              "Failed PUT response for the following:\n" +
                              $"[{uri}]\n" +
                              $"Code: {response.StatusCode}\n" +
                              $"Message: {msg}\n" +
                              $"-----------------------------------------");

            Console.WriteLine(Environment.StackTrace);
        }

        return result;
    }

    /// <summary>
    /// Puts a json resource in the specified uri and returns the response message
    /// </summary>
    public static async Task<TaskResult<T>> PutAsyncWithResponse<T>(string uri, T content, HttpClient http = null)
    {
        if (http is null)
            http = Http;

        JsonContent jsonContent = JsonContent.Create(content);

        var response = await http.PutAsync(BaseAddress + uri, jsonContent);

        TaskResult<T> result = new()
        {
            Success = response.IsSuccessStatusCode,
            Code = (int)response.StatusCode
        };

        if (!result.Success)
        {
            result.Message = await response.Content.ReadAsStringAsync();

            Console.WriteLine("-----------------------------------------\n" +
                              "Failed PUT response for the following:\n" +
                              $"[{uri}]\n" +
                              $"Code: {response.StatusCode}\n" +
                              $"Message: {result.Message}\n" +
                              $"-----------------------------------------");

            Console.WriteLine(Environment.StackTrace);
        }
        else
        {
            if (typeof(T) == typeof(string))
                result.Data = (T)(object)(await response.Content.ReadAsStringAsync());
            else
                result.Data = await JsonSerializer.DeserializeAsync<T>(await response.Content.ReadAsStreamAsync(), DefaultJsonOptions);
        }

        return result;
    }

    /// <summary>
    /// Posts a json resource in the specified uri and returns the response message
    /// </summary>
    public static async Task<TaskResult> PostAsync(string uri, string content, HttpClient http = null)
    {
        if (http is null)
            http = Http;

        StringContent stringContent = null;

        if (content != null)
            stringContent = new StringContent(content);

        var response = await http.PostAsync(BaseAddress + uri, stringContent);
        var msg = await response.Content.ReadAsStringAsync();

        TaskResult result = new()
        {
            Success = response.IsSuccessStatusCode,
            Message = msg,
            Code = (int)response.StatusCode
        };

        if (!result.Success)
        {
            Console.WriteLine("-----------------------------------------\n" +
                              "Failed POST response for the following:\n" +
                              $"[{uri}]\n" +
                              $"Code: {response.StatusCode}\n" +
                              $"Message: {msg}\n" +
                              $"-----------------------------------------");

            Console.WriteLine(Environment.StackTrace);
        }

        return result;
    }

    /// <summary>
    /// Posts a json resource in the specified uri and returns the response message
    /// </summary>
    public static async Task<TaskResult> PostAsync(string uri, object content, HttpClient http = null)
    {
        if (http is null)
            http = Http;

        JsonContent jsonContent = JsonContent.Create(content);

        HttpResponseMessage response;
        
        response = await http.PostAsync(BaseAddress + uri, jsonContent);
        var msg = await response.Content.ReadAsStringAsync();

        TaskResult result = new()
        {
            Success = response.IsSuccessStatusCode,
            Message = msg,
            Code = (int)response.StatusCode
        };

        if (!result.Success)
        {
            Console.WriteLine("-----------------------------------------\n" +
                              "Failed POST response for the following:\n" +
                              $"[{uri}]\n" +
                              $"Code: {response.StatusCode}\n" +
                              $"Message: {result.Message}\n" +
                              $"-----------------------------------------");

            Console.WriteLine(Environment.StackTrace);
        }

        return result;
    }

    /// <summary>
    /// Posts a json resource in the specified uri and returns the response message
    /// </summary>
    public static async Task<TaskResult<T>> PostAsyncWithResponse<T>(string uri, string content, HttpClient http = null)
    {
        if (http is null)
            http = Http;

        StringContent jsonContent = new StringContent(content);

        var response = await http.PostAsync(BaseAddress + uri, jsonContent);

        TaskResult<T> result = new TaskResult<T>()
        {
            Success = response.IsSuccessStatusCode,
            Code = (int)response.StatusCode
        };

        if (!result.Success)
        {
            result.Message = await response.Content.ReadAsStringAsync();

            Console.WriteLine("-----------------------------------------\n" +
                              "Failed POST response for the following:\n" +
                              $"[{uri}]\n" +
                              $"Code: {response.StatusCode}\n" +
                              $"Message: {result.Message}\n" +
                              $"-----------------------------------------");

            Console.WriteLine(Environment.StackTrace);
        }
        else
        {
            result.Message = "Success";

            if (typeof(T) == typeof(string))
                result.Data = (T)(object)(await response.Content.ReadAsStringAsync());
            else
                result.Data = await JsonSerializer.DeserializeAsync<T>(await response.Content.ReadAsStreamAsync(), DefaultJsonOptions);
        }

        return result;
    }

    /// <summary>
    /// Posts a json resource in the specified uri and returns the response message
    /// </summary>
    public static async Task<TaskResult<T>> PostAsyncWithResponse<T>(string uri, HttpClient http = null)
    {
        if (http is null)
            http = Http;

        var response = await http.PostAsync(BaseAddress + uri, null);

        TaskResult<T> result = new TaskResult<T>()
        {
            Success = response.IsSuccessStatusCode,
            Code = (int)response.StatusCode
        };

        if (!result.Success)
        {
            result.Message = await response.Content.ReadAsStringAsync();

            Console.WriteLine("-----------------------------------------\n" +
                              "Failed POST response for the following:\n" +
                              $"[{uri}]\n" +
                              $"Code: {response.StatusCode}\n" +
                              $"Message: {result.Message}\n" +
                              $"-----------------------------------------");

            Console.WriteLine(Environment.StackTrace);
        }
        else
        {
            result.Message = "Success";

            if (typeof(T) == typeof(string))
                result.Data = (T)(object)(await response.Content.ReadAsStringAsync());
            else
                result.Data = await JsonSerializer.DeserializeAsync<T>(await response.Content.ReadAsStreamAsync(), DefaultJsonOptions);
        }

        return result;
    }

    /// <summary>
    /// Posts a multipart resource in the specified uri and returns the response message
    /// </summary>
    public static async Task<TaskResult<T>> PostAsyncWithResponse<T>(string uri, MultipartFormDataContent content, HttpClient http = null)
    {
        if (http is null)
            http = Http;

        var response = await http.PostAsync(BaseAddress + uri, content);

        TaskResult<T> result = new TaskResult<T>()
        {
            Success = response.IsSuccessStatusCode,
            Code = (int)response.StatusCode
        };

        if (!result.Success)
        {
            result.Message = await response.Content.ReadAsStringAsync();

            Console.WriteLine("-----------------------------------------\n" +
                              "Failed POST response for the following:\n" +
                              $"[{uri}]\n" +
                              $"Code: {response.StatusCode}\n" +
                              $"Message: {result.Message}\n" +
                              $"-----------------------------------------");

            Console.WriteLine(Environment.StackTrace);
        }
        else
        {
            result.Message = "Success";

            if (typeof(T) == typeof(string))
                result.Data = (T)(object)(await response.Content.ReadAsStringAsync());
            else
                result.Data = await JsonSerializer.DeserializeAsync<T>(await response.Content.ReadAsStreamAsync(), DefaultJsonOptions);
        }

        return result;
    }


    /// <summary>
    /// Posts a json resource in the specified uri and returns the response message
    /// </summary>
    public static async Task<TaskResult<T>> PostAsyncWithResponse<T>(string uri, object content, HttpClient http = null)
    {
        if (http is null)
            http = Http;

        JsonContent jsonContent = JsonContent.Create(content);

        var response = await http.PostAsync(BaseAddress + uri, jsonContent);

        TaskResult<T> result = new TaskResult<T>()
        {
            Success = response.IsSuccessStatusCode,
            Code = (int)response.StatusCode
        };

        if (!result.Success)
        {
            result.Message = await response.Content.ReadAsStringAsync();

            Console.WriteLine("-----------------------------------------\n" +
                              "Failed POST response for the following:\n" +
                              $"[{uri}]\n" +
                              $"Code: {response.StatusCode}\n" +
                              $"Message: {result.Message}\n" +
                              $"-----------------------------------------");

            Console.WriteLine(Environment.StackTrace);
        }
        else
        {
            result.Message = "Success";

            if (typeof(T) == typeof(string))
                result.Data = (T)(object)(await response.Content.ReadAsStringAsync());
            else
            {
                result.Data = await JsonSerializer.DeserializeAsync<T>(await response.Content.ReadAsStreamAsync(), DefaultJsonOptions);
            }
        }

        return result;
    }

    /// <summary>
    /// Deletes a resource in the specified uri and returns the response message
    /// </summary>
    public static async Task<TaskResult> DeleteAsync(string uri, HttpClient http = null)
    {
        if (http is null)
            http = Http;

        var response = await http.DeleteAsync(BaseAddress + uri);
        var msg = await response.Content.ReadAsStringAsync();

        TaskResult result = new()
        {
            Success = response.IsSuccessStatusCode,
            Message = msg,
            Code = (int)response.StatusCode
        };

        if (!result.Success)
        {
            result.Message = $"An error occured. ({response.StatusCode})";

            Console.WriteLine("-----------------------------------------\n" +
                              "Failed DELETE response for the following:\n" +
                              $"[{uri}]\n" +
                              $"Code: {response.StatusCode}\n" +
                              $"Message: {msg}\n" +
                              $"-----------------------------------------");

            Console.WriteLine(Environment.StackTrace);
        }

        return result;
    }

#endregion
    
    // Sad zone
    public static async Task<TaskResult> DeleteAccountAsync(string password)
    {
        var model = new DeleteAccountModel() { Password = password };
        return await PrimaryNode.PostAsync("api/users/self/hardDelete", model);
    }
}
