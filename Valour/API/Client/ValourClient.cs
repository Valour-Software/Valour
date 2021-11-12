using Microsoft.AspNetCore.SignalR.Client;
using System;
using System.Net.Http.Json;
using System.Text.Json;
using Valour.Api.Authorization.Roles;
using Valour.Api.Extensions;
using Valour.Api.Messages;
using Valour.Api.Planets;
using Valour.Api.Users;
using Valour.Shared;
using Valour.Shared.Items;

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
    private static List<ulong> _joinedPlanetIds;

    /// <summary>
    /// The HttpClient to be used for connections
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
    /// True if SignalR has hooked events
    /// </summary>
    public static bool SignalREventsHooked { get; private set; }

    /// <summary>
    /// Hub connection for SignalR client
    /// </summary>
    public static HubConnection HubConnection { get; private set; }

    /// <summary>
    /// Currently opened planets
    /// </summary>
    public static List<Planet> OpenPlanets { get; private set; }

    /// <summary>
    /// Currently opened channels
    /// </summary>
    public static List<Channel> OpenChannels { get; private set; }

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
    /// Run when SignalR opens a channel
    /// </summary>
    public static event Func<Channel, Task> OnChannelOpen;

    /// <summary>
    /// Run when SignalR closes a channel
    /// </summary>
    public static event Func<Channel, Task> OnChannelClose;

    /// <summary>
    /// Run when a message is recieved
    /// </summary>
    public static event Func<PlanetMessage, Task> OnMessageRecieved;

    /// <summary>
    /// Run when the user logs in
    /// </summary>
    public static event Func<Task> OnLogin;

    public static event Func<Task> OnJoinedPlanetsUpdate;

    #endregion

    static ValourClient()
    {
        // Add victor dummy member
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        ValourCache.Put(ulong.MaxValue, new Member()
        {
            Nickname = "Victor",
            Id = ulong.MaxValue,
            Member_Pfp = "/media/victor-cyan.png"
        });
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

        OpenPlanets = new List<Planet>();
        OpenChannels = new List<Channel>();
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
    public static async Task<Member> GetSelfMember(Planet planet, bool force_refresh = false) =>
        await GetSelfMember(planet.Id, force_refresh);

    /// <summary>
    /// Returns the member for this client's user given a planet id
    /// </summary>
    public static async Task<Member> GetSelfMember(ulong planet_id, bool force_refresh = false) =>
        await Member.FindAsync(planet_id, Self.Id, force_refresh);

    #region SignalR Groups

    /// <summary>
    /// Returns if the given planet is open
    /// </summary>
    public static bool IsPlanetOpen(Planet planet) =>
        OpenPlanets.Any(x => x.Id == planet.Id);

    /// <summary>
    /// Returns if the channel is open
    /// </summary>
    public static bool IsChannelOpen(Channel channel) =>
        OpenChannels.Any(x => x.Id == channel.Id);

    /// <summary>
    /// Opens a SignalR connection to a planet
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

        // Load roles early for cached speed
        await planet.LoadRolesAsync();

        // Load member data early for the same reason (speed)
        await planet.LoadMemberDataAsync();

        // Joins SignalR group
        await HubConnection.SendAsync("JoinPlanet", planet.Id, Token);

        // Load channels and categories
        await planet.LoadChannelsAsync();
        await planet.LoadCategoriesAsync();

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
    public static async Task ClosePlanet(Planet planet)
    {
        // Already closed
        if (!OpenPlanets.Contains(planet))
            return;

        // Close connection
        await HubConnection.SendAsync("LeavePlanet", planet.Id);

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
    public static async Task OpenChannel(Channel channel)
    {
        // Already opened
        if (OpenChannels.Contains(channel))
            return;

        var planet = await channel.GetPlanetAsync();

        // Ensure planet is opened
        await OpenPlanet(planet);

        // Join channel SignalR group
        await HubConnection.SendAsync("JoinChannel", channel.Id, Token);

        // Add to open set
        OpenChannels.Add(channel);

        Console.WriteLine($"Joined SignalR group for channel {channel.Name} ({channel.Id})");

        if (OnChannelOpen is not null)
            await OnChannelOpen.Invoke(channel);
    }

    /// <summary>
    /// Closes a SignalR connection to a channel
    /// </summary>
    public static async Task CloseChannel(Channel channel)
    {
        // Not opened
        if (!OpenChannels.Contains(channel))
            return;

        // Leaves channel SignalR group
        await HubConnection.SendAsync("LeaveChannel", channel.Id);

        // Remove from open set
        OpenChannels.Remove(channel);

        Console.WriteLine($"Left SignalR group for channel {channel.Name} ({channel.Id})");

        if (OnChannelClose is not null)
            await OnChannelClose.Invoke(channel);
    }

    #endregion

    #region SignalR Events

    /// <summary>
    /// Updates an item's properties
    /// </summary>
    public static async Task UpdateItem<T>(T updated, int flags, bool skipEvent = false) where T : Item<T>
    {
        var local = ValourCache.Get<T>(updated.Id);
        if (local != null)
            updated.CopyAllTo(local);

        if (!skipEvent)
        {
            // Invoke specific item update
            await local.InvokeUpdated(flags);

            // Invoke static "any" update
            await local.InvokeAnyUpdated(local, flags);
        }
    }

    /// <summary>
    /// Updates an item's properties
    /// </summary>
    public static async Task DeleteItem<T>(T item) where T : Item<T>
    {
        var local = ValourCache.Get<T>(item.Id);

        ValourCache.Remove<T>(item.Id);

        // Invoke specific item deleted
        await local.InvokeDeleted();

        // Invoke static "any" delete
        await local.InvokeAnyDeleted(local);
    }

    /// <summary>
    /// Ran when a message is recieved
    /// </summary>
    private static async Task MessageRecieved(PlanetMessage message)
    {
        await OnMessageRecieved?.Invoke(message);
    }

    #endregion

    #region Planet Event Handling

    private static void HookPlanetEvents()
    {
        Channel.OnAnyUpdated += OnChannelUpdated;
        Channel.OnAnyDeleted += OnChannelDeleted;

        Category.OnAnyUpdated += OnCategoryUpdated;
        Category.OnAnyDeleted += OnCategoryDeleted;

        Role.OnAnyUpdated += OnRoleUpdated;
        Role.OnAnyDeleted += OnRoleDeleted;
    }

    private static async Task OnChannelUpdated(Channel channel, int flags)
    {
        var planet = await Planet.FindAsync(channel.Planet_Id);

        if (planet is not null)
            await planet.NotifyUpdateChannel(channel);
    }

    private static async Task OnCategoryUpdated(Category category, int flags)
    {
        var planet = await Planet.FindAsync(category.Planet_Id);

        if (planet is not null)
            await planet.NotifyUpdateCategory(category);
    }

    private static async Task OnRoleUpdated(Role role, int flags)
    {
        var planet = await Planet.FindAsync(role.Planet_Id);

        if (planet is not null)
            await planet.NotifyUpdateRole(role);
    }

    private static async Task OnChannelDeleted(Channel channel)
    {
        var planet = await Planet.FindAsync(channel.Planet_Id);

        if (planet is not null)
            await planet.NotifyDeleteChannel(channel);
    }

    private static async Task OnCategoryDeleted(Category category)
    {
        var planet = await Planet.FindAsync(category.Planet_Id);

        if (planet is not null)
            await planet.NotifyDeleteCategory(category);
    }

    private static async Task OnRoleDeleted(Role role)
    {
        var planet = await Planet.FindAsync(role.Planet_Id);

        if (planet is not null)
            await planet.NotifyDeleteRole(role);
    }

    #endregion

    #region Initialization

    /// <summary>
    /// Connects to SignalR hub
    /// </summary>
    public static async Task InitializeSignalR(string hub_uri = "https://valour.gg/planethub")
    {
        await ConnectSignalRHub(hub_uri);
    }

    /// <summary>
    /// Logs in and prepares the client for use
    /// </summary>
    public static async Task<TaskResult<User>> InitializeUser(string token)
    {

        var response = await PostAsyncWithResponse<User>($"api/user/withtoken", token);

        if (!response.Success)
            return response;

        // Set reference to self user
        Self = response.Data;

        // Store token that worked successfully
        _token = token;

        // Add auth header so we never have to do that again
        Http.DefaultRequestHeaders.Add("authorization", Token);

        Console.WriteLine($"Initialized user {Self.Username} ({Self.Id})");

        if (OnLogin != null)
            await OnLogin?.Invoke();

        await LoadJoinedPlanetsAsync();

        return new TaskResult<User>(true, "Success", Self);
    }

    /// <summary>
    /// Should only be run during initialization!
    /// </summary>
    public static async Task LoadJoinedPlanetsAsync()
    {
        var planets = await GetJsonAsync<List<Planet>>($"api/user/{Self.Id}/planets");

        // Add to cache
        foreach (var planet in planets)
        {
            await ValourCache.Put(planet.Id, planet);
        }

        JoinedPlanets = planets;

        _joinedPlanetIds = JoinedPlanets.Select(x => x.Id).ToList();

        if (OnJoinedPlanetsUpdate != null)
            await OnJoinedPlanetsUpdate?.Invoke();
    }

    /// <summary>
    /// Refreshes the user's joined planet list from the server
    /// </summary>
    public static async Task RefreshJoinedPlanetsAsync()
    {
        var planetIds = await GetJsonAsync<List<ulong>>($"api/user/{Self.Id}/planet_ids");

        if (planetIds is null)
            return;

        JoinedPlanets.Clear();

        foreach (var id in planetIds)
        {
            JoinedPlanets.Add(await Planet.FindAsync(id));
        }

        await OnJoinedPlanetsUpdate?.Invoke();
    }

    #endregion

    #region SignalR

    private static async Task ConnectSignalRHub(string hub_url)
    {
        Console.WriteLine("Connecting to Planet Hub");
        Console.WriteLine(hub_url);

        HubConnection = new HubConnectionBuilder()
            .WithUrl(hub_url)
            .WithAutomaticReconnect()
            .Build();

        //hubConnection.KeepAliveInterval = TimeSpan.FromSeconds(30);
        HubConnection.Closed += OnClosed;
        HubConnection.Reconnected += OnReconnect;

        await HubConnection.StartAsync();

        HookSignalREvents();
    }

    private static void HookSignalREvents()
    {
        HubConnection.On<PlanetMessage>("Relay", MessageRecieved);

        HubConnection.On<Planet, int>("PlanetUpdate", (i, d) => UpdateItem(i, d));
        HubConnection.On<Planet>("PlanetDeletion", DeleteItem);

        HubConnection.On<Channel, int>("ChannelUpdate", (i, d) => UpdateItem(i, d));
        HubConnection.On<Channel>("ChannelDeletion", DeleteItem);

        HubConnection.On<Category, int>("CategoryUpdate", (i, d) => UpdateItem(i, d));
        HubConnection.On<Category>("CategoryDeletion", DeleteItem);

        HubConnection.On<Role, int>("RoleUpdate", (i, d) => UpdateItem(i, d));
        HubConnection.On<Role>("RoleDeletion", DeleteItem);

        HubConnection.On<Member, int>("MemberUpdate", (i, d) => UpdateItem(i, d));
        HubConnection.On<Member>("MemberDeletion", DeleteItem);
    }

    /// <summary>
    /// Forces SignalR to refresh the underlying connection
    /// </summary>
    public static async Task ForceRefresh()
    {
        Console.WriteLine("Forcing SignalR refresh.");

        if (HubConnection.State == HubConnectionState.Disconnected)
        {
            Console.WriteLine("Disconnected.");
            await Reconnect();
        }
    }

    /// <summary>
    /// Reconnects the SignalR connection
    /// </summary>
    public static async Task Reconnect()
    {
        // Reconnect
        await HubConnection.StartAsync();
        Console.WriteLine("Reconnecting to Planet Hub");

        await OnReconnect("");
    }

    /// <summary>
    /// Attempt to recover the connection if it is lost
    /// </summary>
    public static async Task OnClosed(Exception e)
    {
        // Ensure disconnect was not on purpose
        if (e != null)
        {
            Console.WriteLine("## A Breaking SignalR Error Has Occured");
            Console.WriteLine("Exception: " + e.Message);
            Console.WriteLine("Stacktrace: " + e.StackTrace);

            await Reconnect();
        }
        else
        {
            Console.WriteLine("SignalR has closed without error.");

            await Reconnect();
        }
    }

    /// <summary>
    /// Run when SignalR reconnects
    /// </summary>
    public static async Task OnReconnect(string data)
    {
        Console.WriteLine("SignalR has reconnected: ");
        Console.WriteLine(data);

        await HandleReconnect();
    }

    /// <summary>
    /// Reconnects to SignalR systems when reconnected
    /// </summary>
    public static async Task HandleReconnect()
    {
        foreach (var planet in OpenPlanets)
        {
            await HubConnection.SendAsync("JoinPlanet", planet.Id, Token);
            Console.WriteLine($"Rejoined SignalR group for planet {planet.Id}");
        }

        foreach (var channel in OpenChannels)
        {
            await HubConnection.SendAsync("JoinChannel", channel.Id, Token);
            Console.WriteLine($"Rejoined SignalR group for channel {channel.Id}");
        }
    }

    #endregion

    #region HTTP Helpers

    /// <summary>
    /// Gets a json resource from the given uri and deserializes it
    /// </summary>
    public static async Task<T> GetJsonAsync<T>(string uri)
    {
        var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);

        T result = default(T);

        if (!response.IsSuccessStatusCode)
        {
            var message = await response.Content.ReadAsStringAsync();

            // This means the null is expected
            if (message == "null"){
                return default(T);
            }

            Console.WriteLine("-----------------------------------------\n" +
                              "Failed GET response for the following:\n" +
                              $"[{uri}]\n" +
                              $"Code: {response.StatusCode}\n" +
                              $"Message: {message}\n" +
                              $"-----------------------------------------");

            Console.WriteLine(Environment.StackTrace);
        }
        else
        {
            if (typeof(T) == typeof(string)) return result;

            result = await JsonSerializer.DeserializeAsync<T>(await response.Content.ReadAsStreamAsync());
        }

        return result;
    }

    /// <summary>
    /// Puts a string resource in the specified uri and returns the response message
    /// </summary>
    public static async Task<TaskResult> PutAsync(string uri, string content)
    {
        StringContent stringContent = new StringContent(content);

        var response = await Http.PutAsync(uri, stringContent);

        TaskResult result = new()
        {
            Message = await response.Content.ReadAsStringAsync(),
            Success = response.IsSuccessStatusCode
        };

        if (!result.Success)
        {
            Console.WriteLine("-----------------------------------------\n" +
                              "Failed PUT response for the following:\n" +
                              $"[{uri}]\n" +
                              $"Code: {response.StatusCode}\n" +
                              $"Message: {await response.Content.ReadAsStringAsync()}\n" +
                              $"-----------------------------------------");

            Console.WriteLine(Environment.StackTrace);
        }

        return result;
    }

    /// <summary>
    /// Puts a json resource in the specified uri and returns the response message
    /// </summary>
    public static async Task<TaskResult> PutAsync(string uri, object content)
    {
        JsonContent jsonContent = JsonContent.Create(content);

        var response = await Http.PutAsync(uri, jsonContent);

        TaskResult result = new()
        {
            Message = await response.Content.ReadAsStringAsync(),
            Success = response.IsSuccessStatusCode
        };

        if (!result.Success)
        {
            Console.WriteLine("-----------------------------------------\n" +
                              "Failed PUT response for the following:\n" +
                              $"[{uri}]\n" +
                              $"Code: {response.StatusCode}\n" +
                              $"Message: {await response.Content.ReadAsStringAsync()}\n" +
                              $"-----------------------------------------");

            Console.WriteLine(Environment.StackTrace);
        }

        return result;
    }

    /// <summary>
    /// Posts a json resource in the specified uri and returns the response message
    /// </summary>
    public static async Task<TaskResult> PostAsync(string uri, string content)
    {
        StringContent stringContent = null;

        if (content != null)
            stringContent = new StringContent(content);

        var response = await Http.PostAsync(uri, stringContent);

        TaskResult result = new()
        {
            Message = await response.Content.ReadAsStringAsync(),
            Success = response.IsSuccessStatusCode
        };

        if (!result.Success)
        {
            Console.WriteLine("-----------------------------------------\n" +
                              "Failed POST response for the following:\n" +
                              $"[{uri}]\n" +
                              $"Code: {response.StatusCode}\n" +
                              $"Message: {await response.Content.ReadAsStringAsync()}\n" +
                              $"-----------------------------------------");

            Console.WriteLine(Environment.StackTrace);
        }

        return result;
    }

    /// <summary>
    /// Posts a json resource in the specified uri and returns the response message
    /// </summary>
    public static async Task<TaskResult> PostAsync(string uri, object content)
    {
        JsonContent jsonContent = JsonContent.Create(content);

        var response = await Http.PostAsync(uri, jsonContent);

        TaskResult result = new()
        {
            Message = await response.Content.ReadAsStringAsync(),
            Success = response.IsSuccessStatusCode
        };

        if (!result.Success)
        {
            Console.WriteLine("-----------------------------------------\n" +
                              "Failed POST response for the following:\n" +
                              $"[{uri}]\n" +
                              $"Code: {response.StatusCode}\n" +
                              $"Message: {await response.Content.ReadAsStringAsync()}\n" +
                              $"-----------------------------------------");

            Console.WriteLine(Environment.StackTrace);
        }

        return result;
    }

    /// <summary>
    /// Posts a json resource in the specified uri and returns the response message
    /// </summary>
    public static async Task<TaskResult<T>> PostAsyncWithResponse<T>(string uri, string content)
    {
        StringContent jsonContent = new StringContent(content);

        var response = await Http.PostAsync(uri, jsonContent);

        TaskResult<T> result = new TaskResult<T>()
        {
            Success = response.IsSuccessStatusCode
        };

        if (!result.Success)
        {
            Console.WriteLine("-----------------------------------------\n" +
                              "Failed POST response for the following:\n" +
                              $"[{uri}]\n" +
                              $"Code: {response.StatusCode}\n" +
                              $"Message: {await response.Content.ReadAsStringAsync()}\n" +
                              $"-----------------------------------------");

            Console.WriteLine(Environment.StackTrace);
        }
        else
        {
            if (typeof(T) == typeof(string))
                result.Data = (T)(object)(await response.Content.ReadAsStringAsync());
            else
                result.Data = await JsonSerializer.DeserializeAsync<T>(await response.Content.ReadAsStreamAsync());
        }

        return result;
    }

    /// <summary>
    /// Posts a multipart resource in the specified uri and returns the response message
    /// </summary>
    public static async Task<TaskResult<T>> PostAsyncWithResponse<T>(string uri, MultipartFormDataContent content)
    {
        var response = await Http.PostAsync(uri, content);

        TaskResult<T> result = new TaskResult<T>()
        {
            Success = response.IsSuccessStatusCode
        };

        if (!result.Success)
        {
            Console.WriteLine("-----------------------------------------\n" +
                              "Failed POST response for the following:\n" +
                              $"[{uri}]\n" +
                              $"Code: {response.StatusCode}\n" +
                              $"Message: {await response.Content.ReadAsStringAsync()}\n" +
                              $"-----------------------------------------");

            Console.WriteLine(Environment.StackTrace);
        }
        else
        {
            if (typeof(T) == typeof(string))
                result.Data = (T)(object)(await response.Content.ReadAsStringAsync());
            else
                result.Data = await JsonSerializer.DeserializeAsync<T>(await response.Content.ReadAsStreamAsync());
        }

        return result;
    }


    /// <summary>
    /// Posts a json resource in the specified uri and returns the response message
    /// </summary>
    public static async Task<TaskResult<T>> PostAsyncWithResponse<T>(string uri, object content)
    {
        JsonContent jsonContent = JsonContent.Create(content);

        var response = await Http.PostAsync(uri, jsonContent);

        TaskResult<T> result = new TaskResult<T>()
        {
            Success = response.IsSuccessStatusCode
        };

        if (!result.Success)
        {
            Console.WriteLine("-----------------------------------------\n" +
                              "Failed POST response for the following:\n" +
                              $"[{uri}]\n" +
                              $"Code: {response.StatusCode}\n" +
                              $"Message: {await response.Content.ReadAsStringAsync()}\n" +
                              $"-----------------------------------------");

            Console.WriteLine(Environment.StackTrace);
        }
        else
        {
            if (typeof(T) == typeof(string))
                result.Data = (T)(object)(await response.Content.ReadAsStringAsync());
            else
                result.Data = await JsonSerializer.DeserializeAsync<T>(await response.Content.ReadAsStreamAsync());
        }

        return result;
    }

    /// <summary>
    /// Deletes a resource in the specified uri and returns the response message
    /// </summary>
    public static async Task<TaskResult> DeleteAsync(string uri)
    {
        var response = await Http.DeleteAsync(uri);

        TaskResult result = new()
        {
            Message = await response.Content.ReadAsStringAsync(),
            Success = response.IsSuccessStatusCode
        };

        if (!result.Success)
        {
            Console.WriteLine("-----------------------------------------\n" +
                              "Failed PUT response for the following:\n" +
                              $"[{uri}]\n" +
                              $"Code: {response.StatusCode}\n" +
                              $"Message: {await response.Content.ReadAsStringAsync()}\n" +
                              $"-----------------------------------------");

            Console.WriteLine(Environment.StackTrace);
        }

        return result;
    }

    #endregion
}