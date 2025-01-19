using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Valour.Sdk.Client;
using Valour.Sdk.ModelLogic;
using Valour.Sdk.Services;
using Valour.Shared;

namespace Valour.Sdk.Nodes;

public class Node : ServiceBase // each node acts like a service
{
    /// <summary>
    /// The name of this node
    /// </summary>
    public string Name { get; private set; }

    /// <summary>
    /// The HttpClient for this node. Should be configured to send requests only to this node
    /// </summary>
    public HttpClient HttpClient { get; private set; }

    /// <summary>
    /// Hub connection for SignalR client
    /// </summary>
    public HubConnection HubConnection { get; private set; }

    /// <summary>
    /// True if this node is currently reconnecting
    /// </summary>
    public bool IsReconnecting { get; private set; }

    /// <summary>
    /// True if this is the primary node for this client
    /// </summary>
    public bool IsPrimary { get; private set; }

    public static readonly JsonSerializerOptions DefaultJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Timer that updates the user's online status
    /// </summary>
    private static Timer _onlineTimer;

    public ValourClient Client { get; private set; }
    private readonly NodeService _nodeService;

    public Node(ValourClient client)
    {
        Client = client;
        _nodeService = client.NodeService;
    }

    public async Task<TaskResult> InitializeAsync(string name, bool isPrimary = false)
    {
        Name = name;
        IsPrimary = isPrimary;

        var logOptions = new LogOptions(
            "Node " + Name,
            "#036bfc",
            "#fc0356",
            "#fc8403"
        );

        SetupLogging(Client.Logger, logOptions);

        Client.NodeService.RegisterNode(this);

        if (Client.HttpClientProvider is not null)
        {
            HttpClient = Client.HttpClientProvider.GetHttpClient();
        }
        else
        {
            HttpClient = new HttpClient();
        }

        HttpClient.BaseAddress = new Uri(Client.BaseAddress);

        // Set header for node
        HttpClient.DefaultRequestHeaders.Add("X-Server-Select", Name);
        
        if (Client.AuthService.Token is null)
        {
            LogWarning("No token found, skipping full node initialization. Only registration will be possible.");
            return TaskResult.SuccessResult;
        }
        
        HttpClient.DefaultRequestHeaders.Add("Authorization", Client.AuthService.Token);

        Log("Setting up new hub connection...");

        await ConnectSignalRHub();
        
        var authResult = await AuthenticateSignalR();
        if (!authResult.Success)
            return authResult;
        
        await ConnectToUserChannel();

        BeginPings();
        
        return TaskResult.SuccessResult;
    }

    private async Task ConnectToUserChannel()
    {
        TaskResult userResult;
        int tries = 0;

        do
        {
            userResult = await ConnectToUserSignalRChannel();
            if (!userResult.Success)
            {
                // TODO: This should probably retry
                LogError($"Error connecting to User SignalR channel (Retry {tries})");
                LogError(userResult.Message);
                await Task.Delay(3000);
            }
        } while (!userResult.Success);


        Log("Connected to user channel for SignalR.");
    }

    /// <summary>
    /// Starts pinging for online state
    /// </summary>
    private void BeginPings()
    {
        Log("Beginning online pings...");
        _onlineTimer = new Timer(OnPingTimer, null, TimeSpan.Zero, TimeSpan.FromSeconds(60));
    }

    private readonly Stopwatch _pingStopwatch = new();

    /// <summary>
    /// Run by the online timer
    /// </summary>
    private async void OnPingTimer(object state = null)
    {
        if (HubConnection.State != HubConnectionState.Connected)
        {
            LogError($"Ping failed. Hub state is: {HubConnection.State.ToString()}");
            return;
        }

        try
        {
            Log("Doing node ping...");

            _pingStopwatch.Reset();
            _pingStopwatch.Start();
            var response = await HubConnection.InvokeAsync<string>("ping", IsPrimary);
            _pingStopwatch.Stop();

            if (response == "pong")
            {
                Log($"Pinged successfully in {_pingStopwatch.ElapsedMilliseconds}ms");
            }
            else
            {
                LogError($"Ping failed. Response: {response}");
            }
        }
        catch (Exception ex)
        {
            LogError("Ping failed.", ex);
        }
    }

    #region SignalR

    private async Task ConnectSignalRHub()
    {
        var address = Client.BaseAddress + "hubs/core";

        Log("Connecting to Core hub at " + address);

        HubConnection = new HubConnectionBuilder()
            .WithUrl(address, options => {
                {
                    options.Headers.Add("X-Server-Select", Name);
                    options.UseStatefulReconnect = true;
                    
                    // Support in-memory testing
                    if (Client.HttpClientProvider is not null)
                    {
                        options.HttpMessageHandlerFactory = (_ => Client.HttpClientProvider.GetHttpMessageHandler());
                        options.Transports = HttpTransportType.LongPolling;
                    }
                }
            })
            .Build();

        HubConnection.ServerTimeout = TimeSpan.FromSeconds(20);

        HubConnection.Closed += OnSignalRClosed;
        HubConnection.Reconnected += OnHubReconnect;

        await HubConnection.StartAsync();

        HookSignalREvents();

        // Call event so services can hook into hub events
        _nodeService.NodeAdded?.Invoke(this);
    }

    private async Task<TaskResult> AuthenticateSignalR()
    {
        Log("Authenticating with SignalR hub...");

        var response = new TaskResult(false, "Failed to authorize. This is a critical SignalR error.");

        var authorized = false;
        var tries = 0;

        while (!authorized && tries < 5)
        {
            if (tries > 0)
                await Task.Delay(3000);

            try
            {
                response = await HubConnection.InvokeAsync<TaskResult>("Authorize", Client.AuthService.Token);
                authorized = response.Success;

                // Token invalid or expired. Clear 
                if (response.Code == 401)
                {
                    return response;
                }
            }
            catch (Exception ex)
            {
                LogError("Failed to authorize with SignalR.", ex);
            }

            tries++;
        }

        if (!authorized)
        {
            Log("** FATAL: Failed to authorize with SignalR after 5 attempts. **");
            return new TaskResult(false, "Failed to authorize with SignalR after 5 attempts.");
        }

        Log(response.Message);
        return TaskResult.SuccessResult;
    }

    private void HookModelEvents<TModel>(TModel model)
        where TModel : ClientModel<TModel>
    {
        var typeName = nameof(TModel);
        HubConnection.On<TModel, int>($"{typeName}-Update", OnModelUpdate<TModel>);
        HubConnection.On<TModel>($"{typeName}-Delete", OnModelDelete<TModel>);
    }

    /// <summary>
    /// Specific model update event
    /// </summary>
    private void OnModelUpdate<TModel>(TModel model, int flags)
        where TModel : ClientModel<TModel>
    {
        Client.Cache.Sync(model, true, flags);
    }

    private void OnModelDelete<TModel>(TModel model)
        where TModel : ClientModel<TModel>
    {
        Client.Cache.Delete(model);
    }

    private static bool IsSubclassOfRawGeneric(Type genericBaseType, Type toCheck)
    {
        while (toCheck != null && toCheck != typeof(object))
        {
            var currentType = toCheck.IsGenericType ? toCheck.GetGenericTypeDefinition() : toCheck;
            if (currentType == genericBaseType)
            {
                return true;
            }

            toCheck = toCheck.BaseType;
        }

        return false;
    }


    public void HookSignalREvents()
    {
        Log("Hooking model events.");

        // For every single item...
        var baseType = typeof(ClientModel<>);

        // Get all non-abstract types that inherit from ClientModel<> at any level
        var derivedTypes = Assembly.GetAssembly(baseType)!.GetTypes()
            .Where(t =>
                !t.IsAbstract && // Exclude abstract classes
                IsSubclassOfRawGeneric(baseType, t) && // Check inheritance from ClientModel<>
                t != baseType) // Exclude the base class itself
            .ToList();

        // Get the method info
        var hookMethodInfo = this.GetType()
            .GetMethod(nameof(HookModelEvents), BindingFlags.NonPublic | BindingFlags.Instance);

        foreach (var type in derivedTypes)
        {
            try
            {
                var modelType = type; // TModel

                // Make the generic method
                var genericMethod = hookMethodInfo!.MakeGenericMethod(modelType);

                // Create an instance of the model
                var modelInstance = Activator.CreateInstance(modelType);

                // Invoke the generic method
                genericMethod.Invoke(this, [modelInstance]);

                Log($"Registered ClientModel type: {type.Name}");
            }
            catch (Exception ex)
            {
                LogError($"Failed to hook model type {type.Name}", ex);
            }
        }

        HubConnection.On<Notification>("RelayNotification", Client.NotificationService.OnNotificationReceived);
        HubConnection.On("RelayNotificationsCleared", Client.NotificationService.OnNotificationsCleared);
        HubConnection.On<FriendEventData>("RelayFriendEvent", Client.FriendService.OnFriendEventReceived);


        Log("Item Events hooked.");
    }

    /// <summary>
    /// Forces SignalR to refresh the underlying connection
    /// </summary>
    public void CheckConnection()
    {
        LogWarning("Refresh has been requested.");
        LogWarning("SignalR state is " + HubConnection.State);

        if (HubConnection.State == HubConnectionState.Disconnected)
        {
            LogError("Disconnect has been detected. Reconnecting...");
            _ = Reconnect();
        }
    }

    /// <summary>
    /// Reconnects the SignalR connection
    /// </summary>
    private async Task Reconnect()
    {
        if (IsReconnecting)
            return;

        IsReconnecting = true;

        try
        {
            // Test connection if it thinks it's safe
            if (HubConnection.State == HubConnectionState.Connected)
            {
                _ = await HubConnection.InvokeAsync<string>("ping");
            }
        }
        catch (System.Exception ex)
        {
            LogError("Hub reports connection, but ping failed. Will attempt reconnect...", ex);
        }

        while (HubConnection.State == HubConnectionState.Disconnected)
        {
            await Task.Delay(3000);

            Log("Reconnecting to Core Hub...");

            try
            {
                await HubConnection.StartAsync();
            }
            catch (System.Exception ex)
            {
                LogError("Failed to reconnect... waiting three seconds to continue.", ex);
            }
        }

        await OnHubReconnect("Success");

        IsReconnecting = false;
    }

    /// <summary>
    /// Attempt to recover the connection if it is lost
    /// </summary>
    private Task OnSignalRClosed(Exception ex)
    {
        // Ensure disconnect was not on purpose
        if (ex is not null)
        {
            LogError("A Breaking SignalR Error Has Occured", ex);
            return Reconnect();
        }

        LogError("SignalR has closed without error.");
        // return Reconnect(); // TODO: Shouldn't we... not reconnect if it was on purpose?
        return Task.CompletedTask;
    }

    /// <summary>
    /// Run when SignalR reconnects
    /// </summary>
    private async Task OnHubReconnect(string data)
    {
        LogWarning("SignalR has reconnected: " + data);
        await HandleReconnect();
        Client.NodeService.NodeReconnected?.Invoke(this);
    }

    /// <summary>
    /// Reconnects to SignalR systems when reconnected
    /// </summary>
    public async Task HandleReconnect()
    {
        // Authenticate and connect to personal channel
        await AuthenticateSignalR();
        await ConnectToUserSignalRChannel();
    }

    private Task<TaskResult> ConnectToUserSignalRChannel()
    {
        return HubConnection.InvokeAsync<TaskResult>("JoinUser", IsPrimary);
    }

    #endregion

    #region HTTP Helpers

    private (string node, long? planetId) ReadMisdirectInfo(string info)
    {
        if (string.IsNullOrEmpty(info))
            return (null, null);

        var parts = info.Split(':');
        return (parts[0], parts.Length > 1 ? long.Parse(parts[1]) : null);
    }

    private async Task<Node> HandleMisdirect(HttpResponseMessage response, string method, string uri)
    {
        var info = ReadMisdirectInfo(await response.Content.ReadAsStringAsync());
        LogError(
            $"Wrong node! {method} {uri} - {Name} is not the correct node for this request. Forwarding to {info}.");

        // Load the correct node
        return await Client.NodeService.GetNodeAndSetPlanetLocation(info.node, info.planetId);
    }

    /// <summary>
    /// Gets a JSON resource from the given URI and deserializes it.
    /// </summary>
    public async Task<TaskResult<T>> GetJsonAsync<T>(
        string uri,
        bool allow404 = false,
        int? cacheDurationMs = 100,
        int retries = 0
    )
    {
        var cache = LazyGetRequestCache<T>.Cache;

        // 1) Check if we already have a Lazy<Task<TaskResult<T>>> in the dictionary.
        if (cache.TryGetValue(uri, out var existingLazy))
        {
            // If so, just await the same task (reuse it).
            return await existingLazy.Value;
        }

        // 2) Otherwise, create a new Lazy<Task<TaskResult<T>>> that will do the real HTTP call:
        var newLazy = new Lazy<Task<TaskResult<T>>>(() =>
            ActuallyGetJsonAsync<T>(uri, allow404, retries)
        );

        // 3) Attempt to add our new Lazy to the dictionary.
        if (cache.TryAdd(uri, newLazy))
        {
            try
            {
                // This line actually invokes the real request once.
                var result = await newLazy.Value;

                if (cacheDurationMs is not null)
                {
                    // 4A) Schedule removal from the cache after "cacheDurationMs".
                    _ = Task.Delay(cacheDurationMs.Value).ContinueWith(_ => { cache.TryRemove(uri, out var _); });
                }
                else
                {
                    // 4B) Or remove it immediately if no cache duration was specified.
                    cache.TryRemove(uri, out var _);
                }

                return result;
            }
            catch
            {
                // If the underlying request fails, remove from dictionary so the next call can retry.
                cache.TryRemove(uri, out _);
                throw;
            }
        }
        else
        {
            // Another thread beat us to it, so we just await the existing task.
            return await cache[uri].Value;
        }
    }

    /// <summary>
    /// Actually performs the GET request and deserializes the response.
    /// </summary>
    private async Task<TaskResult<T>> ActuallyGetJsonAsync<T>(string uri, bool allow404, int retries)
    {
        if (retries > 3)
        {
            LogError($"Failed 3 retries - GET {uri}");
            return TaskResult<T>.FromFailure("Failed after 3 retries.");
        }

        try
        {
            var response =
                await HttpClient.GetAsync(Client.BaseAddress + uri, HttpCompletionOption.ResponseHeadersRead);
            if (response.IsSuccessStatusCode)
            {
                return await TryDeserializeResponse<T>(response, uri);
            }

            // Handle 404 if allowed
            if (response.StatusCode == HttpStatusCode.NotFound && allow404)
            {
                return TaskResult<T>.FromData(default);
            }

            // MisdirectedRequest? (Forward to correct node, etc.)
            if (response.StatusCode == HttpStatusCode.MisdirectedRequest)
            {
                var correctNode = await HandleMisdirect(response, "GET", uri);
                if (correctNode is not null)
                    return await correctNode.GetJsonAsync<T>(uri, allow404, retries + 1);

                return TaskResult<T>.FromFailure("Failed to find correct node.");
            }

            var msg = await response.Content.ReadAsStringAsync();
            LogError($"{response.StatusCode} - GET {uri}: \n{msg}");
            return TaskResult<T>.FromFailure(msg, (int)response.StatusCode);
        }
        catch (HttpRequestException ex)
        {
            LogError($"Critical HTTP Failure - GET {uri}:", ex);
            return TaskResult<T>.FromFailure(ex);
        }
    }

    /// <summary>
    /// Gets a JSON resource from the given URI as a string.
    /// </summary>
    public async Task<TaskResult<string>> GetAsync(string uri, bool allow404 = false, int retries = 0)
    {
        if (retries > 3)
        {
            LogError($"Failed 3 retries - GET {uri}");
            return TaskResult<string>.FromFailure("Failed after 3 retries.");
        }

        try
        {
            var response =
                await HttpClient.GetAsync(Client.BaseAddress + uri, HttpCompletionOption.ResponseHeadersRead);
            var msg = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
                return TaskResult<string>.FromData(msg);

            // Handle 404 if allowed
            if (response.StatusCode == HttpStatusCode.NotFound && allow404)
            {
                return TaskResult<string>.FromData(msg);
            }

            // Wrong node! it returned the correct node name so we can actually forward this.
            if (response.StatusCode == HttpStatusCode.MisdirectedRequest)
            {
                var correctNode = await HandleMisdirect(response, "GET", uri);
                if (correctNode is not null)
                    return await correctNode.GetAsync(uri, allow404, retries + 1);

                return TaskResult<string>.FromFailure("Failed to find correct node.");
            }

            LogError($"{response.StatusCode} - GET {uri}: \n{msg}");
            return TaskResult<string>.FromFailure(msg, (int)response.StatusCode);
        }
        catch (HttpRequestException ex)
        {
            LogError($"Critical HTTP Failure - GET {uri}:", ex);
            return TaskResult<string>.FromFailure(ex);
        }
    }

    /// <summary>
    /// Puts a JSON resource in the specified URI and returns the deserialized response.
    /// </summary>
    public async Task<TaskResult<T>> PutAsyncWithResponse<T>(string uri, object content, int retries = 0)
    {
        if (retries > 3)
        {
            LogError($"Failed 3 retries - PUT {uri}");
            return TaskResult<T>.FromFailure("Failed after 3 retries.");
        }

        var jsonContent = JsonContent.Create(content);

        try
        {
            var response = await HttpClient.PutAsync(Client.BaseAddress + uri, jsonContent);

            if (response.IsSuccessStatusCode)
                return await TryDeserializeResponse<T>(response, uri);

            // Wrong node! it returned the correct node name so we can actually forward this.
            if (response.StatusCode == HttpStatusCode.MisdirectedRequest)
            {
                var correctNode = await HandleMisdirect(response, "PUT", uri);
                if (correctNode is not null)
                    return await correctNode.PutAsyncWithResponse<T>(uri, retries + 1);

                return TaskResult<T>.FromFailure("Failed to find correct node.");
            }

            var msg = await response.Content.ReadAsStringAsync();
            LogError($"{response.StatusCode} - PUT {uri}: \n{msg}");

            return TaskResult<T>.FromFailure(msg, (int)response.StatusCode);
        }
        catch (HttpRequestException ex)
        {
            LogError($"Critical HTTP Failure - PUT {uri}:", ex);
            return TaskResult<T>.FromFailure(ex);
        }
    }

    /// <summary>
    /// Posts a JSON resource to the specified URI and returns the deserialized response.
    /// </summary>
    public async Task<TaskResult<T>> PostAsyncWithResponse<T>(string uri, object content, int retries = 0)
    {
        if (retries > 3)
        {
            LogError($"Failed 3 retries - POST {uri}");
            return TaskResult<T>.FromFailure("Failed after 3 retries.");
        }

        var jsonContent = JsonContent.Create(content);

        try
        {
            var response = await HttpClient.PostAsync(Client.BaseAddress + uri, jsonContent);

            if (response.IsSuccessStatusCode)
                return await TryDeserializeResponse<T>(response, uri);

            // Wrong node! it returned the correct node name so we can actually forward this.
            if (response.StatusCode == HttpStatusCode.MisdirectedRequest)
            {
                var correctNode = await HandleMisdirect(response, "POST", uri);
                if (correctNode is not null)
                    return await correctNode.PostAsyncWithResponse<T>(uri, retries + 1);

                return TaskResult<T>.FromFailure("Failed to find correct node.");
            }

            var msg = await response.Content.ReadAsStringAsync();
            LogError($"{response.StatusCode} - POST {uri}: \n{msg}");

            return TaskResult<T>.FromFailure(msg, (int)response.StatusCode);
        }
        catch (HttpRequestException ex)
        {
            LogError($"Critical HTTP Failure - POST {uri}:", ex);
            return TaskResult<T>.FromFailure(ex);
        }
    }

    /// <summary>
    /// Posts an empty request to the specified URI and returns the deserialized response.
    /// </summary>
    public async Task<TaskResult<T>> PostAsyncWithResponse<T>(string uri, int retries = 0)
    {
        if (retries > 3)
        {
            LogError($"Failed 3 retries - POST {uri}");
            return TaskResult<T>.FromFailure("Failed after 3 retries.");
        }

        try
        {
            var response = await HttpClient.PostAsync(Client.BaseAddress + uri, null);

            if (response.IsSuccessStatusCode)
                return await TryDeserializeResponse<T>(response, uri);

            // Wrong node! it returned the correct node name so we can actually forward this.
            if (response.StatusCode == HttpStatusCode.MisdirectedRequest)
            {
                var correctNode = await HandleMisdirect(response, "POST", uri);
                if (correctNode is not null)
                    return await correctNode.PostAsyncWithResponse<T>(uri, retries + 1);

                return TaskResult<T>.FromFailure("Failed to find correct node.");
            }

            var msg = await response.Content.ReadAsStringAsync();
            LogError($"{response.StatusCode} - POST {uri}: \n{msg}");

            return TaskResult<T>.FromFailure(msg, (int)response.StatusCode);
        }
        catch (HttpRequestException ex)
        {
            LogError($"Critical HTTP Failure - POST {uri}:", ex);
            return TaskResult<T>.FromFailure(ex);
        }
    }

    /// <summary>
    /// Puts a JSON resource in the specified URI and returns the response message.
    /// </summary>
    public async Task<TaskResult> PutAsync(string uri, string content, int retries = 0)
    {
        if (retries > 3)
        {
            LogError($"Failed 3 retries - PUT {uri}");
            return TaskResult.FromFailure("Failed after 3 retries.");
        }

        var stringContent = new StringContent(content);

        try
        {
            var response = await HttpClient.PutAsync(Client.BaseAddress + uri, stringContent);
            var msg = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
                return TaskResult.FromSuccess(msg);

            // Wrong node! it returned the correct node name so we can actually forward this.
            if (response.StatusCode == HttpStatusCode.MisdirectedRequest)
            {
                var correctNode = await HandleMisdirect(response, "PUT", uri);
                if (correctNode is not null)
                    return await correctNode.PutAsync(uri, content, retries + 1);

                return TaskResult.FromFailure("Failed to find correct node.");
            }

            LogError($"{response.StatusCode} - PUT {uri}: \n{msg}");

            return TaskResult.FromFailure(msg, (int)response.StatusCode);
        }
        catch (HttpRequestException ex)
        {
            LogError($"Critical HTTP Failure - PUT {uri}:", ex);
            return TaskResult.FromFailure(ex);
        }
    }

    /// <summary>
    /// Posts a string resource to the specified URI and returns the response message.
    /// </summary>
    public async Task<TaskResult> PostAsync(string uri, string content, int retries = 0)
    {
        if (retries > 3)
        {
            LogError($"Failed 3 retries - POST {uri}");
            return TaskResult.FromFailure("Failed after 3 retries.");
        }

        var stringContent = content is null ? null : new StringContent(content);

        try
        {
            var response = await HttpClient.PostAsync(Client.BaseAddress + uri, stringContent);
            var msg = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
                return TaskResult.FromSuccess(msg);

            // Wrong node! it returned the correct node name so we can actually forward this.
            if (response.StatusCode == HttpStatusCode.MisdirectedRequest)
            {
                var correctNode = await HandleMisdirect(response, "POST", uri);
                if (correctNode is not null)
                    return await correctNode.PostAsync(uri, retries + 1);

                return TaskResult.FromFailure("Failed to find correct node.");
            }

            LogError($"{response.StatusCode} - POST {uri}: \n{msg}");
            return TaskResult.FromFailure(msg, (int)response.StatusCode);
        }
        catch (HttpRequestException ex)
        {
            LogError($"Critical HTTP Failure - POST {uri}:", ex);
            return TaskResult.FromFailure(ex);
        }
    }

    /// <summary>
    /// Posts a resource to the specified URI and returns the status.
    /// </summary>
    public async Task<TaskResult> PostAsync<T>(string uri, T content, int retries = 0)
    {
        if (retries > 3)
        {
            LogError($"Failed 3 retries - POST {uri}");
            return TaskResult.FromFailure("Failed after 3 retries.");
        }

        // create json content
        var jsonContent = JsonContent.Create(content);

        try
        {
            var response = await HttpClient.PostAsync(Client.BaseAddress + uri, jsonContent);
            var msg = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
                return TaskResult.FromSuccess(msg);

            // Wrong node! it returned the correct node name so we can actually forward this.
            if (response.StatusCode == HttpStatusCode.MisdirectedRequest)
            {
                var correctNode = await HandleMisdirect(response, "POST", uri);
                if (correctNode is not null)
                    return await correctNode.PostAsync<T>(uri, content, retries + 1);

                return TaskResult.FromFailure("Failed to find correct node.");
            }

            LogError($"{response.StatusCode} - POST {uri}: \n{msg}");
            return TaskResult.FromFailure(msg, (int)response.StatusCode);
        }
        catch (HttpRequestException ex)
        {
            LogError($"Critical HTTP Failure - POST {uri}:", ex);
            return TaskResult.FromFailure(ex);
        }
    }

    /// <summary>
    /// Deletes a resource from the specified URI and returns the response message.
    /// </summary>
    public async Task<TaskResult> DeleteAsync(string uri, int retries = 0)
    {
        if (retries > 3)
        {
            LogError($"Failed 3 retries - DELETE {uri}");
            return TaskResult.FromFailure("Failed after 3 retries.");
        }

        try
        {
            var response = await HttpClient.DeleteAsync(Client.BaseAddress + uri);
            var msg = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
                return TaskResult.FromSuccess(msg);

            // Wrong node! it returned the correct node name so we can actually forward this.
            if (response.StatusCode == HttpStatusCode.MisdirectedRequest)
            {
                var correctNode = await HandleMisdirect(response, "DELETE", uri);
                if (correctNode is not null)
                    return await correctNode.DeleteAsync(uri, retries + 1);

                return TaskResult.FromFailure("Failed to find correct node.");
            }

            LogError($"{response.StatusCode} - DELETE {uri}: \n{msg}");
            return TaskResult.FromFailure(msg, (int)response.StatusCode);
        }
        catch (HttpRequestException ex)
        {
            LogError($"Critical HTTP Failure - DELETE {uri}:", ex);
            return TaskResult.FromFailure(ex);
        }
    }

    /// <summary>
    /// Attempts to deserialize the JSON response data into the specified type and returns a TaskResult.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async Task<TaskResult<T>> TryDeserializeResponse<T>(HttpResponseMessage response, string uri)
    {
        try
        {
            var data = await JsonSerializer.DeserializeAsync<T>(await response.Content.ReadAsStreamAsync(),
                DefaultJsonOptions);
            return TaskResult<T>.FromData(data);
        }
        catch (JsonException ex)
        {
            LogError($"Bad JSON response for {uri}", ex);
            return TaskResult<T>.FromFailure(ex);
        }
    }

    #endregion
}