using Microsoft.AspNetCore.SignalR.Client;
using System;
using System.Net.Http.Json;
using System.Text.Json;
using Valour.Api.Planets;
using Valour.Api.Users;
using Valour.Shared;

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
    public static List<Planet> JoinedPlanets { get; }

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
    /// Hub connection for SignalR client
    /// </summary>
    public static HubConnection HubConnection { get; private set; }

    /// <summary>
    /// Currently opened planets
    /// </summary>
    public static HashSet<ulong> OpenPlanetIds { get; private set; }

    /// <summary>
    /// Currently opened channels
    /// </summary>
    public static HashSet<ulong> OpenChannelIds {  get; private set; }

    #region Events

    /// <summary>
    /// Run when SignalR opens a planet
    /// </summary>
    public static event Func<Planet, Task> OnPlanetOpen;

    /// <summary>
    /// Run when SignalR closes a planet
    /// </summary>
    public static event Func<Planet, Task> OnPlanetClose;

    #endregion

    #region SignalR channels

    /// <summary>
    /// Returns if the given planet is open
    /// </summary>
    public static bool IsPlanetOpen(Planet planet) =>
        OpenPlanetIds.Contains(planet.Id);

    /// <summary>
    /// Returns if the channel is open
    /// </summary>
    public static bool IsChannelOpen(Channel channel) =>
        OpenChannelIds.Contains(channel.Id);

    /// <summary>
    /// Returns the currently open planets of the client
    /// </summary>
    public static async Task<List<Planet>> GetOpenPlanetsAsync()
    {
        List<Planet> planets = new();

        foreach (ulong id in OpenPlanetIds)
        {
            var res = await Planet.FindAsync(id);
            if (res.Success)
                planets.Add(res.Data);
        }

        return planets;
    }

    /// <summary>
    /// Returns the currently open channels of the client
    /// </summary>
    public static async Task<List<Channel>> GetOpenChannelsAsync() 
    {
        List<Channel> channels = new();

        foreach (ulong id in OpenChannelIds)
        {
            var res = await Channel.FindAsync(id);
            if (res.Success)
                channels.Add(res.Data);
        }

        return channels;
    }

    /// <summary>
    /// Opens a SignalR connection to a planet
    /// </summary>
    public static async Task OpenPlanet(Planet planet)
    {
        // Cannot open null
        if (planet == null)
            return;

        // Already open
        if (OpenPlanetIds.Contains(planet.Id))
            return;

        // Mark as opened
        OpenPlanetIds.Add(planet.Id);

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
        if (!OpenPlanetIds.Contains(planet.Id))
            return;

        // Close connection
        await HubConnection.SendAsync("LeavePlanet", planet.Id);

        // Remove from list
        OpenPlanetIds.Remove(planet.Id);

        Console.WriteLine($"Left SignalR group for planet {planet.Name} ({planet.Id})");

        // Invoke event
        if (OnPlanetClose is not null)
        {
            Console.WriteLine($"Invoking close planet event for {planet.Name} ({planet.Id})");
            await OnPlanetClose.Invoke(planet);
        }
    }

    public static async Task OpenChannel(Channel channel)
    {
        // Not opened yet
        if (!OpenChannelIds.Contains(channel.Id))
        {
            var planet = await channel.GetPlanetAsync();
        }
            

        

    }

    #endregion

    /// <summary>
    /// Logs in and prepares the client for use
    /// </summary>
    public static async Task<TaskResult<User>> Initialize(string token, string hub_uri = "https://valour.gg/planethub")
    {
        // First connect to SignalR hub
        await ConnectSignalRHub(hub_uri);

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

        return new TaskResult<User>(true, "Success", Self);
    }

    /// <summary>
    /// Returns the joined planets of the client user
    /// </summary>
    public static async Task<TaskResult<List<Planet>>> GetJoinedPlanetsAsync()
    {
        var planets = new List<Planet>();

        foreach (var id in _joinedPlanetIds)
        {
            var planetResponse = await Planet.FindAsync(id);

            if (!planetResponse.Success)
                return new TaskResult<List<Planet>>(false, planetResponse.Message);

            planets.Add(planetResponse.Data);
        }

        return new TaskResult<List<Planet>>(true, "Success", planets);
    }

    /// <summary>
    /// Loads the user's joined planet list from the server
    /// </summary>
    public static async Task<TaskResult> LoadJoinedPlanetsAsync()
    {
        var response = await GetJsonAsync<List<ulong>>($"api/user/{Self.Id}/planet_ids");

        if (!response.Success)
            return new TaskResult(false, response.Message);

        _joinedPlanetIds = response.Data;

        return new TaskResult(true, "Success");
    }

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
        foreach (ulong id in OpenPlanetIds)
        {
            await HubConnection.SendAsync("JoinPlanet", id, Token);
            Console.WriteLine($"Rejoined SignalR group for planet {id}");
        }

        foreach (ulong id in OpenChannelIds)
        {
            await HubConnection.SendAsync("JoinChannel", id, Token);
            Console.WriteLine($"Rejoined SignalR group for channel {id}");
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

            Console.WriteLine("-----------------------------------------\n" +
                              "Failed GET response for the following:\n" +
                              $"[{uri}]" +
                              $"Code: {response.StatusCode}" +
                              $"Message: {message}\n" +
                              $"-----------------------------------------");
        }
        else
        {
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
                              $"[{uri}]" +
                              $"Code: {response.StatusCode}" +
                              $"Message: {await response.Content.ReadAsStringAsync()}\n" +
                              $"-----------------------------------------");
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
                              $"[{uri}]" +
                              $"Code: {response.StatusCode}" +
                              $"Message: {await response.Content.ReadAsStringAsync()}\n" +
                              $"-----------------------------------------");
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
                              $"[{uri}]" +
                              $"Code: {response.StatusCode}" +
                              $"Message: {await response.Content.ReadAsStringAsync()}\n" +
                              $"-----------------------------------------");
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
                              $"[{uri}]" +
                              $"Code: {response.StatusCode}" +
                              $"Message: {await response.Content.ReadAsStringAsync()}\n" +
                              $"-----------------------------------------");
        }
        else
        {
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
                              $"[{uri}]" +
                              $"Code: {response.StatusCode}" +
                              $"Message: {await response.Content.ReadAsStringAsync()}\n" +
                              $"-----------------------------------------");
        }

        return result;
    }

    #endregion
}