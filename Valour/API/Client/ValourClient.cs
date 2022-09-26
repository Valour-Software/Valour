using Microsoft.AspNetCore.SignalR.Client;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Xml.Linq;
using Valour.Api.Extensions;
using Valour.Api.Items;
using Valour.Api.Items.Authorization;
using Valour.Api.Items.Channels;
using Valour.Api.Items.Channels.Planets;
using Valour.Api.Items.Messages;
using Valour.Api.Items.Planets;
using Valour.Api.Items.Planets.Members;
using Valour.Api.Items.Users;
using Valour.Api.Nodes;
using Valour.Shared;
using Valour.Shared.Items.Channels;
using Valour.Shared.Items.Users;

namespace Valour.Api.Client;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

public static class ValourClient
{
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
    /// Currently opened channels
    /// </summary>
    public static List<PlanetChatChannel> OpenPlanetChannels { get; private set; }

    /// <summary>
    /// The state of channels this user has access to
    /// </summary>
    public static Dictionary<long, UserChannelState> ChannelStates { get; private set; } = new();

    /// <summary>
    /// The primary node this client is connected to
    /// </summary>
    public static Node PrimaryNode { get; set; }

    /// <summary>
    /// The friends of this client
    /// </summary>
    public static List<User> Friends { get; set; }

    #region Event Fields

    /// <summary>
    /// Run when SignalR opens a planet
    /// </summary>
    public static event Func<Planet, Task> OnPlanetOpen;

    /// <summary>
    /// Run when SignalR closes a planet
    /// </summary>
    public static event Func<Planet, Task> OnPlanetClose;

    /// <summary>
    /// Run when a UserChannelState is updated
    /// </summary>
    public static event Func<UserChannelState, Task> OnUserChannelStateUpdate;

    /// <summary>
    /// Run when SignalR opens a channel
    /// </summary>
    public static event Func<PlanetChatChannel, Task> OnChannelOpen;

    /// <summary>
    /// Run when SignalR closes a channel
    /// </summary>
    public static event Func<PlanetChatChannel, Task> OnChannelClose;

    /// <summary>
    /// Run when a message is recieved
    /// </summary>
    public static event Func<Message, Task> OnMessageRecieved;

    /// <summary>
    /// Run when a planet is deleted
    /// </summary>
    public static event Func<PlanetMessage, Task> OnMessageDeleted;

#if (!DEBUG)
    public static string BaseAddress => "https://app.valour.gg/";
#else
    public static string BaseAddress => "https://localhost:44331/";
#endif

    /// <summary>
    /// Run when the user logs in
    /// </summary>
    public static event Func<Task> OnLogin;

    public static event Func<Task> OnJoinedPlanetsUpdate;

    public static event Func<Node, Task> OnNodeReconnect;

    public static readonly JsonSerializerOptions DefaultJsonOptions = new JsonSerializerOptions()
    {
        PropertyNameCaseInsensitive = true
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
            MemberPfp = "/media/victor-cyan.png"
        });
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

        OpenPlanets = new List<Planet>();
        OpenPlanetChannels = new List<PlanetChatChannel>();
        JoinedPlanets = new List<Planet>();

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
    public static ValueTask<PlanetMember> GetSelfMember(Planet planet, bool force_refresh = false) =>
        GetSelfMember(planet.Id, force_refresh);

    /// <summary>
    /// Returns the member for this client's user given a planet id
    /// </summary>
    public static ValueTask<PlanetMember> GetSelfMember(long planetId, bool force_refresh = false) =>
        PlanetMember.FindAsyncByUser(Self.Id, planetId, force_refresh);

    /// <summary>
    /// Sends a message
    /// </summary>
    public static async Task<TaskResult> SendMessage(PlanetMessage message)
        => await message.PostMessageAsync();

    /// <summary>
    /// Adds a friend
    /// </summary>
    public static async Task<TaskResult<UserFriend>> AddFriendAsync(string username)
    {
        var result = await PrimaryNode.PostAsyncWithResponse<UserFriend>($"api/{nameof(UserFriend)}/add/{username}");

        if (result.Success)
        {
            var newFriendUser = await User.FindAsync(result.Data.FriendId);
            Friends.Add(newFriendUser);
        }

        return result;
    }

    /// <summary>
    /// Removes a friend
    /// </summary>
    public static async Task<TaskResult> RemoveFriendAsync(string username)
    {
        var result = await PrimaryNode.PostAsync($"api/{nameof(UserFriend)}/remove/{username}", null);

        if (result.Success)
        {
            var friend = Friends.FirstOrDefault(x => x.Name.ToLower() == username.ToLower());
            if (friend is not null)
                Friends.Remove(friend);
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
    public static bool IsChannelOpen(PlanetChatChannel channel) =>
        OpenPlanetChannels.Any(x => x.Id == channel.Id);

    /// <summary>
    /// Opens a planet and prepares it for use
    /// </summary>
    public static async Task OpenPlanet(Planet planet)
    {
        // Cannot open null
        if (planet == null)
            return;

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

        // Load channels and categories
        tasks.Add(planet.LoadChannelsAsync());
        tasks.Add(planet.LoadCategoriesAsync());

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
    /// Closes a SignalR connection to a planet
    /// </summary>
    public static async Task ClosePlanetConnection(Planet planet)
    {
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
    /// Opens a SignalR connection to a channel
    /// </summary>
    public static async Task OpenPlanetChannel(PlanetChatChannel channel)
    {
        // Already opened
        if (OpenPlanetChannels.Contains(channel))
            return;

        var planet = await channel.GetPlanetAsync();

        // Ensure planet is opened
        await OpenPlanet(planet);

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
    public static async Task ClosePlanetChannel(PlanetChatChannel channel)
    {
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

        await ClosePlanetConnectionIfNoChannels(await channel.GetPlanetAsync());
    }

    /// <summary>
    /// Closes planet connection if no chat channels are opened for it
    /// </summary>
    public static async Task ClosePlanetConnectionIfNoChannels(Planet planet)
    {
        if (planet == null)
            return;

        // Check if any open chat windows are for the planet
        if (!OpenPlanetChannels.Any(x => x.PlanetId == planet.Id))
        {
            // Close the planet connection
            await ClosePlanetConnection(planet);
        }
    }

    #endregion

    #region SignalR Events

    public static async Task RefreshNodes()
    {
        foreach (var node in NodeManager.Nodes)
        {
            await node.ForceRefresh();
        }
    }

    public static async Task NotifyNodeReconnect(Node node)
    {
        await OnNodeReconnect?.Invoke(node);
    }

    public static async Task UpdateChannelState(ChannelStateUpdate update)
    {
        // Right now only planet chat channels have state updates
        var channel = ValourCache.Get<PlanetChatChannel>(update.ChannelId);
        if (channel is null)
            return;

        channel.State = update.State;

        // If the channel is currently open, we also update the user's channel state.
        // Because they can see the channel.
        // That makes sense, right?
        // Right?
        /*
        if (OpenChannels.Any(x => x.Id == channel.Id))
        {
            ChannelStates.TryGetValue(channel.Id, out UserChannelState state);

            if (state != null)
            {
                state.LastViewedState = channel.State;
            }
            else
            {
                ChannelStates.Add(channel.Id, new UserChannelState()
                {
                    LastViewedState = channel.State,
                    UserId = Self.Id,
                    ChannelId = channel.Id
                });
            }
        }
        */

        await channel.OnUpdate(0x01);
        await ItemObserver<PlanetChatChannel>.InvokeAnyUpdated(channel, false, 0x01);
    }

    /// <summary>
    /// Updates an item's properties
    /// </summary>
    public static async Task UpdateItem<T>(T updated, int flags, bool skipEvent = false) where T : Item
    {
        // printing to console is SLOW, only turn on for debugging reasons
        //Console.WriteLine("Update for " + updated.Id + ",  skipEvent is " + skipEvent);

        var local = ValourCache.Get<T>(updated.Id);

        if (local != null)
            updated.CopyAllTo(local);

        if (!skipEvent)
        {
            if (local != null) {
                await local.InvokeUpdatedEventAsync(flags);
                await ItemObserver<T>.InvokeAnyUpdated(local, false, flags);
            }
            else {
                await updated.AddToCache();
                await ItemObserver<T>.InvokeAnyUpdated(updated, true, flags);
            }

            // printing to console is SLOW, only turn on for debugging reasons
            //Console.WriteLine("Invoked update events for " + updated.Id);
        }
    }

    /// <summary>
    /// Updates an item's properties
    /// </summary>
    public static async Task DeleteItem<T>(T item) where T : Item
    {
        // Console.WriteLine($"Deletion for {item.Id}, type {item.GetType()}");
        var local = ValourCache.Get<T>(item.Id);

        ValourCache.Remove<T>(item.Id);

        if (local is null)
        {
            // Invoke static "any" delete
            await item.InvokeDeletedEventAsync();
            await ItemObserver<T>.InvokeAnyDeleted(item);
        }
        else
        {
            // Invoke static "any" delete
            await local.InvokeDeletedEventAsync();
            await ItemObserver<T>.InvokeAnyDeleted(local);
        }
    }

    /// <summary>
    /// Ran when a message is recieved
    /// </summary>
    public static async Task MessageRecieved(Message message)
    {
        // Console.WriteLine("Received message " + message.Id);
        await ValourCache.Put(message.Id, message);
        await OnMessageRecieved?.Invoke(message);
    }

    public static async Task MessageDeleted(PlanetMessage message)
    {
        await OnMessageDeleted?.Invoke(message);
    }

    #endregion

    #region Planet Event Handling

    private static void HookPlanetEvents()
        {
            ItemObserver<PlanetChatChannel>.OnAnyUpdated += OnChannelUpdated;
            ItemObserver<PlanetChatChannel>.OnAnyDeleted += OnChannelDeleted;

            ItemObserver<PlanetCategoryChannel>.OnAnyUpdated += OnCategoryUpdated;
            ItemObserver<PlanetCategoryChannel>.OnAnyDeleted += OnCategoryDeleted;

            ItemObserver<PlanetRole>.OnAnyUpdated += OnRoleUpdated;
            ItemObserver<PlanetRole>.OnAnyDeleted += OnRoleDeleted;

            ItemObserver<PlanetRoleMember>.OnAnyUpdated += OnMemberRoleUpdated;
            ItemObserver<PlanetRoleMember>.OnAnyDeleted += OnMemberRoleDeleted;

            ItemObserver<Planet>.OnAnyDeleted += OnPlanetDeleted;
        }

        private static async Task OnPlanetDeleted(Planet planet)
        {
            _joinedPlanetIds.Remove(planet.Id);
            JoinedPlanets = JoinedPlanets.Where(x => x.Id != planet.Id).ToList();
            await ClosePlanetConnection(planet);
        }

        private static async Task OnMemberRoleUpdated(PlanetRoleMember rolemember, bool newitem, int flags)
        {
            var planet = await Planet.FindAsync(rolemember.PlanetId);

            if (planet is not null)
            {
                var member = await PlanetMember.FindAsync(rolemember.MemberId, rolemember.PlanetId);
                if (!await member.HasRoleAsync(rolemember.RoleId))
                {
                    var roleids = (await member.GetRolesAsync()).Select(x => x.Id).ToList();
                    roleids.Add(rolemember.RoleId);
                    await member.SetLocalRoleIds(roleids);
                }
                await ItemObserver<PlanetMember>.InvokeAnyUpdated(member, false, PlanetMember.FLAG_UPDATE_ROLES);
                await member.InvokeUpdatedEventAsync(PlanetMember.FLAG_UPDATE_ROLES);
            }
        }

        private static async Task OnMemberRoleDeleted(PlanetRoleMember rolemember)
        {
            var planet = await Planet.FindAsync(rolemember.PlanetId);

            if (planet is not null)
            {
                var member = await PlanetMember.FindAsync(rolemember.MemberId, rolemember.PlanetId);
                if (await member.HasRoleAsync(rolemember.RoleId))
                {
                    var roleids = (await member.GetRolesAsync()).Select(x => x.Id).ToList();
                    roleids.Remove(rolemember.RoleId);
                    await member.SetLocalRoleIds(roleids);
                }
                await ItemObserver<PlanetMember>.InvokeAnyUpdated(member, false, PlanetMember.FLAG_UPDATE_ROLES);
                await member.InvokeUpdatedEventAsync(PlanetMember.FLAG_UPDATE_ROLES);
            }
        }

        private static async Task OnChannelUpdated(PlanetChatChannel channel, bool newItem, int flags)
        {
            var planet = await Planet.FindAsync(channel.PlanetId);

            if (planet is not null)
                await planet.NotifyUpdateChannel(channel);
        }

        private static async Task OnCategoryUpdated(PlanetCategoryChannel category, bool newItem, int flags)
        {
            var planet = await Planet.FindAsync(category.PlanetId);

            if (planet is not null)
                await planet.NotifyUpdateCategory(category);
        }

        private static async Task OnRoleUpdated(PlanetRole role, bool newItem, int flags)
        {
            var planet = await Planet.FindAsync(role.PlanetId);

            if (planet is not null)
                await planet.NotifyUpdateRole(role);
        }

        private static async Task OnChannelDeleted(PlanetChatChannel channel)
        {
            var planet = await Planet.FindAsync(channel.PlanetId);

            if (planet is not null)
                await planet.NotifyDeleteChannel(channel);
        }

        private static async Task OnCategoryDeleted(PlanetCategoryChannel category)
        {
            var planet = await Planet.FindAsync(category.PlanetId);

            if (planet is not null)
                await planet.NotifyDeleteCategory(category);
        }

        private static async Task OnRoleDeleted(PlanetRole role)
        {
            var planet = await Planet.FindAsync(role.PlanetId);

            if (planet is not null)
                await planet.NotifyDeleteRole(role);
        }

    #endregion

    #region Initialization

    /// <summary>
    /// Gets the Token for the client
    /// </summary>
    public static async Task<TaskResult<string>> GetToken(string email, string password)
    {
        TokenRequest request = new()
        {
            Email = email,
            Password = password
        };

        var response = await PostAsyncWithResponse<AuthToken>($"api/user/token", request);

        if (!response.Success)
        {
            Console.WriteLine("Failed to request user token.");
            Console.WriteLine(response.Message);
            return new TaskResult<string>(false, $"Incorrect email or password. (Are you using your email?)", response.Message);
        }

        var token = response.Data.Id;

        _token = token;

        return new TaskResult<string>(true, "Success", _token);
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

        // Get user

        var response = await GetJsonAsync<User>($"api/user/self");

        if (!response.Success)
            return response;

        // Set reference to self user
        Self = response.Data;

        // Now that we have our user, it should have node data we can use to set up our first node
        // Initialize primary node
        PrimaryNode = new Node();
        await PrimaryNode.InitializeAsync(Self.NodeName, _token);

        // Set node to primary node for main http client
        Http.DefaultRequestHeaders.Add("X-Server-Select", PrimaryNode.Name);

        var loadTasks = new List<Task>()
        {
            LoadChannelStatesAsync(),
            LoadFriendsAsync(),
            LoadJoinedPlanetsAsync()
        };

        // Load user data concurrently
        await Task.WhenAll(loadTasks);

        if (OnLogin != null)
            await OnLogin?.Invoke();

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

        // Get user

        var response = await GetJsonAsync<User>($"api/user/self");

        if (!response.Success)
            return response;

        // Set reference to self user
        Self = response.Data;

        // Now that we have our user, it should have node data we can use to set up our first node
        // Initialize primary node
        PrimaryNode = new Node();
        await PrimaryNode.InitializeAsync(Self.NodeName, _token);

        // Add auth header so we never have to do that again
        Http.DefaultRequestHeaders.Add("authorization", Token);
        // Set node to primary node for main http client
        Http.DefaultRequestHeaders.Add("X-Server-Select", PrimaryNode.Name);

        Console.WriteLine($"Initialized bot {Self.Name} ({Self.Id})");

        if (OnLogin != null)
            await OnLogin?.Invoke();

        await JoinAllChannelsAsync();

        return new TaskResult<User>(true, "Success", Self);
    }

    /// <summary>
    /// Should only be run during initialization of bots!
    /// </summary>
    public static async Task JoinAllChannelsAsync()
    {
        // Get all joined planets
        var planets = (await PrimaryNode.GetJsonAsync<List<Planet>>("api/user/self/planets")).Data;

        // Add to cache
        foreach (var planet in planets)
        {
            await ValourCache.Put(planet.Id, planet);

            OpenPlanet(planet);

            var channels = await planet.GetChannelsAsync();

            channels.ForEach(async x => await OpenPlanetChannel(x));
        }

        JoinedPlanets = planets;

        _joinedPlanetIds = JoinedPlanets.Select(x => x.Id).ToList();

        if (OnJoinedPlanetsUpdate != null)
            await OnJoinedPlanetsUpdate?.Invoke();
    }

    public static async Task UpdateUserChannelState(UserChannelState channelState)
    {

        if (ChannelStates.ContainsKey(channelState.ChannelId))
            ChannelStates[channelState.ChannelId].LastViewedState = channelState.LastViewedState;
        else
            ChannelStates.Add(channelState.ChannelId, channelState);

        // Access dict again to maintain references (do not try to optimize and break everything)
        await OnUserChannelStateUpdate.Invoke(ChannelStates[channelState.ChannelId]);
    }
    
    public static async Task LoadChannelStatesAsync()
    {
        var response = await PrimaryNode.GetJsonAsync<List<UserChannelState>>($"api/user/self/channelstates");
        if (!response.Success)
        {
            Console.WriteLine("** Failed to load channel states **");
            Console.WriteLine(response.Message);

            return;
        }

        foreach (var state in response.Data)
        {
            ChannelStates[state.ChannelId] = state;
        }

        Console.WriteLine("Loaded " + ChannelStates.Count + " channel states.");
    }

    /// <summary>
    /// Should only be run during initialization!
    /// </summary>
    public static async Task LoadJoinedPlanetsAsync()
    {
        var response = await PrimaryNode.GetJsonAsync<List<Planet>>($"api/user/self/planets");

        if (!response.Success)
            return;

        var planets = response.Data;

        // Add to cache
        foreach (var planet in planets)
            await ValourCache.Put(planet.Id, planet);

        JoinedPlanets = planets;

        _joinedPlanetIds = JoinedPlanets.Select(x => x.Id).ToList();

        if (OnJoinedPlanetsUpdate != null)
            await OnJoinedPlanetsUpdate?.Invoke();
    }

    public static async Task LoadFriendsAsync()
    {
        var friendResult = await Self.GetFriendsAsync();

        if (!friendResult.Success)
        {
            await Logger.Log("Error loading friends.", "red");
            await Logger.Log(friendResult.Message, "red");
            return;
        }

        Friends = friendResult.Data;

        foreach (var friend in Friends)
            await ValourCache.Put(friend.Id, friend);

        await Logger.Log($"Loaded {friendResult.Data.Count} friends.", "cyan");
    }

    public static async Task<List<Planet>> GetDiscoverablePlanetsAsync()
    {
        var response = await PrimaryNode.GetJsonAsync<List<Planet>>($"api/planet/discoverable");
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
        var response = await PrimaryNode.GetJsonAsync<List<long>>($"api/user/self/planetIds");

        if (!response.Success)
            return;

        var planetIds = response.Data;

        JoinedPlanets.Clear();

        foreach (var id in planetIds)
        {
            JoinedPlanets.Add(await Planet.FindAsync(id));
        }

        await OnJoinedPlanetsUpdate?.Invoke();
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
            Data = default(T)
        };

        if (!response.IsSuccessStatusCode)
        {
            result.Message = await response.Content.ReadAsStringAsync();

            // This means the null is expected
            if (allowNull && response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return new TaskResult<T>(false, $"An error occured. ({response.StatusCode})");

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
            Message = msg
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
            Message = msg
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
            Message = msg
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

        try
        {
            response = await http.PostAsync(BaseAddress + uri, jsonContent);
        }
        catch (System.Exception)
        {
            return new TaskResult(false, "Unable to reach server.");
        }

        var msg = await response.Content.ReadAsStringAsync();

        TaskResult result = new()
        {
            Success = response.IsSuccessStatusCode,
            Message = msg
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

        StringContent jsonContent = new StringContent((string)content);

        var response = await http.PostAsync(BaseAddress + uri, jsonContent);

        TaskResult<T> result = new TaskResult<T>()
        {
            Success = response.IsSuccessStatusCode
        };

        if (!result.Success)
        {
            result.Message = await response.Content.ReadAsStringAsync(); ;

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
            Success = response.IsSuccessStatusCode
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
            Success = response.IsSuccessStatusCode
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
            Success = response.IsSuccessStatusCode
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
            Message = msg
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
}
