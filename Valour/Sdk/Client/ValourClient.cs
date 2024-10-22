using System.Net.Http.Json;
using System.Text.Json;
using Valour.Sdk.Models.Messages.Embeds;
using Valour.Sdk.Models.Economy;
using Valour.Sdk.Nodes;
using Valour.SDK.Services;
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
    /// The primary node this client is connected to
    /// </summary>
    public static Node PrimaryNode { get; set; }
    
    /// <summary>
    /// Pain and suffering for thee
    /// </summary>
    public static List<Notification> UnreadNotifications { get; set; }
    
    /// <summary>
    /// A set from the source of notifications to the notification.
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
    /// TODO: Nothing browser-related should be in SDK.
    /// </summary>
    public static event Func<Task> OnRefocus;

    

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
    /// Run when a category is reordered
    /// </summary>
    public static event Func<CategoryOrderEvent, Task> OnCategoryOrderUpdate;

    /// <summary>
    /// Run when the user logs in
    /// </summary>
    public static event Func<Task> OnLogin;

    public static event Func<Node, Task> OnNodeReconnect;

    public static readonly JsonSerializerOptions DefaultJsonOptions = new JsonSerializerOptions()
    {
        PropertyNameCaseInsensitive = true,
    };

#endregion

    static ValourClient()
    {
        UnreadNotifications = new();
        UnreadNotificationsLookup = new();
    }

    /// <summary>
    /// Sets the HTTP client
    /// </summary>
    public static void SetHttpClient(HttpClient client) => _httpClient = client;

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

	#region SignalR Groups
    
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
        var cached = notification.Sync();   
        
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

    // TODO: change
    public static async Task HandleCategoryOrderUpdate(CategoryOrderEvent eventData)
    {
        // Update channels in cache
        uint pos = 0;
        foreach (var data in eventData.Order)
        {
            if (Channel.Cache.TryGet(data.Id, out var channel))
            {
                Console.WriteLine($"{pos}: {channel.Name}");

                // The parent can be changed in this event
                channel.ParentId = eventData.CategoryId;

                // Position can be changed in this event
                channel.RawPosition = pos;
            }

            pos++;
        }
        
        if (OnCategoryOrderUpdate is not null)
            await OnCategoryOrderUpdate.Invoke(eventData);
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
            FriendService.FetchesFriendsAsync(),
            PlanetService.FetchJoinedPlanetsAsync(),
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
                await ModelCache<,>.Put(planet.Id, planet);

                await OpenPlanetConnection(planet, "bot-init");
                
                foreach (var channel in planet.Channels)
                {
                    await TryOpenPlanetChannelConnection(channel, "bot-init");
                }
            }));
        }

        await Task.WhenAll(planetTasks);

        JoinedPlanets = planets;

        _joinedPlanetIds = JoinedPlanets.Select(x => x.Id).ToList();

        if (OnJoinedPlanetsUpdate != null)
            await OnJoinedPlanetsUpdate.Invoke();
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
            
            await channel.AddToCacheAsync(channel);
        }

        if (DirectChatChannels is null)
            DirectChatChannels = new();
        else
            DirectChatChannels.Clear();

        // This second step is necessary because we need to ensure we only use the cache objects
        foreach (var channel in response.Data)
        {
            DirectChatChannels.Add(ModelCache<,>.Get<Channel>(channel.Id));
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
            var cached = ModelCache<,>.Get<Notification>(notification.Id);
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

    public static async Task<List<Planet>> GetDiscoverablePlanetsAsync()
    {
        var response = await PrimaryNode.GetJsonAsync<List<Planet>>($"api/planets/discoverable");
        if (!response.Success)
            return new List<Planet>();

        var planets = response.Data;

        foreach (var planet in planets)
            await ModelCache<,>.Put(planet.Id, planet, true);

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
            JoinedPlanets.Add(await Planet.FetchAsync(id));
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
    public static async Task<TaskResult<T>> PutAsyncWithResponse<T>(string uri, object content, HttpClient http = null)
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

    public static async Task<TaskResult> UpdatePasswordAsync(string oldPassword, string newPassword) {
        var model = new ChangePasswordRequest() { OldPassword = oldPassword, NewPassword = newPassword };
        return await PrimaryNode.PostAsync("api/users/self/password", model);
    }
    
    // Sad zone
    public static async Task<TaskResult> DeleteAccountAsync(string password)
    {
        var model = new DeleteAccountModel() { Password = password };
        return await PrimaryNode.PostAsync("api/users/self/hardDelete", model);
    }
}
