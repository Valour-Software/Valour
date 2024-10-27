using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Valour.Sdk.Client;
using Valour.Sdk.ModelLogic;
using Valour.Sdk.Models.Messages.Embeds;
using Valour.SDK.Services;
using Valour.Shared;
using Valour.Shared.Channels;
using Valour.Shared.Models;

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
    /// True if this node is ready to send and recieve requests
    /// </summary>
    public bool IsReady { get; private set; }
    
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
    
    public Node(ValourClient client)
    {
        Client = client;
    }
    
    public async Task InitializeAsync(string name, bool isPrimary = false)
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

        Client.NodeService.AddNode(this);

        HttpClient = new HttpClient();
        HttpClient.BaseAddress = new Uri(Client.BaseAddress);

        // Set header for node
        HttpClient.DefaultRequestHeaders.Add("X-Server-Select", Name);
        HttpClient.DefaultRequestHeaders.Add("Authorization", Client.AuthService.Token);

        Log("Setting up new hub connection...");

        await ConnectSignalRHub();
        await AuthenticateSignalR();
        await ConnectToUserChannel();
        
        IsReady = true;
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
        var address = _client.BaseAddress + "hubs/core";

        Log("Connecting to Core hub at " + address);

        HubConnection = new HubConnectionBuilder()
        .WithUrl(address, options =>
        {
            options.Headers.Add("X-Server-Select", Name); 
            options.UseStatefulReconnect = true;
        })
        .Build();

        HubConnection.ServerTimeout = TimeSpan.FromSeconds(20);

        HubConnection.Closed += OnSignalRClosed;
        HubConnection.Reconnected += OnHubReconnect;

        await HubConnection.StartAsync();

        HookSignalREvents();
        BeginPings();
    }

    private async Task AuthenticateSignalR()
    {
        Log("Authenticating with SignalR hub...");

        var response = new TaskResult(false, "Failed to authorize. This is a critical SignalR error.");

        var authorized = false;
        var tries = 0;
        
        while (!authorized && tries < 5)
        {
            if (tries > 0)
                await Task.Delay(3000);

            response = await HubConnection.InvokeAsync<TaskResult>("Authorize", _client.AuthService.Token);
            authorized = response.Success;
            tries++;
        }

        if (!authorized)
        {
            Log("** FATAL: Failed to authorize with SignalR after 5 attempts. **");
        }

        Log(response.Message);
    }

    private void HookModelEvents<TModel, TId>(TModel model, TId id)
        where TModel : ClientModel<TModel, TId>
        where TId : IEquatable<TId>
    {
        var typeName = nameof(TModel);
        HubConnection.On<TModel, int>($"{typeName}-Update", OnModelUpdate<TModel, TId>);
        HubConnection.On<TModel>($"{typeName}-Delete", OnModelDelete<TModel, TId>);
    }

    /// <summary>
    /// Specific model update event
    /// </summary>
    private void OnModelUpdate<TModel, TId>(TModel model, int flags)
        where TModel : ClientModel<TModel, TId>
        where TId : IEquatable<TId>
    {
        model.Sync(true, flags);
    }
    
    private void OnModelDelete<TModel, TId>(TModel model)
        where TModel : ClientModel<TModel, TId>
        where TId : IEquatable<TId>
    {
        ModelUpdater.DeleteItem<TModel, TId>(model);
    }

    public void HookSignalREvents()
    {
        Log("Hooking model events.");

        // For every single item...
        var baseType = typeof(ClientModel<,>);
        
        // Filter types that inherit from the generic ClientModel<>
        var derivedTypes = Assembly.GetAssembly(baseType)!.GetTypes()
            .Where(t => 
                t.BaseType != null && 
                t.BaseType.IsGenericType && 
                t.BaseType.GetGenericTypeDefinition() == baseType)
            .ToList();
        
        foreach (var type in derivedTypes)
        {
            var genericArguments = type.BaseType!.GetGenericArguments();

            var modelType = type; // TModel
            var idType = genericArguments[1]; // TId

            // Get the method info
            var methodInfo = this.GetType()
                .GetMethod(nameof(HookModelEvents), BindingFlags.NonPublic | BindingFlags.Instance);

            // Make the generic method
            var genericMethod = methodInfo!.MakeGenericMethod(modelType, idType);

            // Create an instance of the model
            var modelInstance = Activator.CreateInstance(modelType);

            // Get the Id value
            var idProperty = modelType.GetProperty("Id");
            var idValue = idProperty!.GetValue(modelInstance);

            // Invoke the generic method
            genericMethod.Invoke(this, [modelInstance, idValue]);
            
            Log($"Registered ClientModel type: {type.Name}");
        }

        HubConnection.On<Message>("Relay", _client.MessageService.OnPlanetMessageReceived);
        HubConnection.On<Message>("RelayEdit", _client.MessageService.OnPlanetMessageEdited);
        HubConnection.On<Message>("RelayDirect", _client.MessageService.OnDirectMessageReceived);
        HubConnection.On<Message>("RelayDirectEdit", _client.MessageService.OnDirectMessageEdited);
        HubConnection.On<Notification>("RelayNotification", _client.NotificationService.OnNotificationReceived);
        HubConnection.On("RelayNotificationsCleared", _client.NotificationService.OnNotificationsCleared);
        HubConnection.On<FriendEventData>("RelayFriendEvent", _client.FriendService.OnFriendEventReceived);
        
        HubConnection.On<Message>("DeleteMessage", _client.MessageService.OnMessageDeleted);
        HubConnection.On<ChannelStateUpdate>("Channel-State", _client.ChannelStateService.OnChannelStateUpdated);
        HubConnection.On<UserChannelState>("UserChannelState-Update", _client.ChannelStateService.OnUserChannelStateUpdated);
        HubConnection.On<ChannelWatchingUpdate>("Channel-Watching-Update", _client.HandleChannelWatchingUpdateRecieved);
        HubConnection.On<ChannelTypingUpdate>("Channel-CurrentlyTyping-Update", _client.HandleChannelCurrentlyTypingUpdateRecieved);
        HubConnection.On<PersonalEmbedUpdate>("Personal-Embed-Update", _client.HandlePersonalEmbedUpdate);
		HubConnection.On<ChannelEmbedUpdate>("Channel-Embed-Update", _client.HandleChannelEmbedUpdate);
        HubConnection.On<CategoryOrderEvent>("CategoryOrder-Update", _client.HandleCategoryOrderUpdate);

		Log("Item Events hooked.");
    }

    /// <summary>
    /// Forces SignalR to refresh the underlying connection
    /// </summary>
    public void ForceRefresh()
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
        _client.NodeService.NodeReconnected?.Invoke(this);
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

    /// <summary>
    /// Gets a JSON resource from the given URI and deserializes it.
    /// </summary>
    public async Task<TaskResult<T>> GetJsonAsync<T>(string uri, bool allow404 = false)
    {
        try
        {
            var response = await HttpClient.GetAsync(_client.BaseAddress + uri, HttpCompletionOption.ResponseHeadersRead);
            
            if (response.IsSuccessStatusCode)
            {
                return await TryDeserializeResponse<T>(response, uri);
            }

            // Handle 404 if allowed
            if (response.StatusCode == HttpStatusCode.NotFound && allow404)
            {
                return TaskResult<T>.FromData(default);
            }

            // Log and return error message
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
    /// Gets a JSON resource from the given URI as a string.
    /// </summary>
    public async Task<TaskResult<string>> GetAsync(string uri, bool allow404 = false)
    {
        try
        {
            var response = await HttpClient.GetAsync(_client.BaseAddress + uri, HttpCompletionOption.ResponseHeadersRead);
            var msg = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
                return TaskResult<string>.FromData(msg);
            
            // Handle 404 if allowed
            if (response.StatusCode == HttpStatusCode.NotFound && allow404)
            {
                return TaskResult<string>.FromData(msg);
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
public async Task<TaskResult<T>> PutAsyncWithResponse<T>(string uri, object content)
{
    var jsonContent = JsonContent.Create(content);

    try
    {
        var response = await HttpClient.PutAsync(_client.BaseAddress + uri, jsonContent);

        if (response.IsSuccessStatusCode)
            return await TryDeserializeResponse<T>(response, uri);

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
public async Task<TaskResult<T>> PostAsyncWithResponse<T>(string uri, object content)
{
    var jsonContent = JsonContent.Create(content);

    try
    {
        var response = await HttpClient.PostAsync(_client.BaseAddress + uri, jsonContent);

        if (response.IsSuccessStatusCode)
            return await TryDeserializeResponse<T>(response, uri);

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
public async Task<TaskResult<T>> PostAsyncWithResponse<T>(string uri)
{
    try
    {
        var response = await HttpClient.PostAsync(_client.BaseAddress + uri, null);

        if (response.IsSuccessStatusCode)
            return await TryDeserializeResponse<T>(response, uri);

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
public async Task<TaskResult> PutAsync(string uri, string content)
{
    var stringContent = new StringContent(content);

    try
    {
        var response = await HttpClient.PutAsync(_client.BaseAddress + uri, stringContent);
        var msg = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode)
            return TaskResult.FromSuccess(msg);

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
public async Task<TaskResult> PostAsync(string uri, string content)
{
    var stringContent = new StringContent(content);

    try
    {
        var response = await HttpClient.PostAsync(_client.BaseAddress + uri, stringContent);
        var msg = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode)
            return TaskResult.FromSuccess(msg);

        LogError($"{response.StatusCode} - GET {uri}: \n{msg}");
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
public async Task<TaskResult> DeleteAsync(string uri)
{
    try
    {
        var response = await HttpClient.DeleteAsync(_client.BaseAddress + uri);
        var msg = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode)
            return TaskResult.FromSuccess(msg);

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
            var data = await JsonSerializer.DeserializeAsync<T>(await response.Content.ReadAsStreamAsync(), DefaultJsonOptions);
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
