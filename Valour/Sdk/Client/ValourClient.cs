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

public class ValourClient
{
    //////////////
    // Services //
    //////////////
    
    public BotService BotService { get; private set; }
    public AuthService AuthService { get; private set; }
    public ChannelStateService ChannelStateService { get; private set; }
    public FriendService FriendService { get; private set; }
    public MessageService MessageService { get; private set; }
    public NodeService NodeService { get; private set; }
    public PlanetService PlanetService { get; private set; }
    public PlanetChannelService PlanetChannelService { get; private set; }
    public DirectChannelService DirectChannelService { get; private set; }
    public TenorService TenorService { get; private set; }
    public LoggingService Logger { get; private set; }
    public SubscriptionService SubscriptionService { get; private set; }
    public NotificationService NotificationService { get; private set; }
    
    /// <summary>
    /// The base address the client is connected to
    /// </summary>
    public string BaseAddress { get; private set; }

    /// <summary>
    /// The user for this client instance
    /// </summary>
    public User Self { get; set; }

    /// <summary>
    /// The HttpClient to be used for general requests (no node!)
    /// </summary>
    public HttpClient Http => _httpClient;

    /// <summary>
    /// The internal HttpClient
    /// </summary>
    private HttpClient _httpClient;

    /// <summary>
    /// True if the client is logged in
    /// </summary>
    public bool IsLoggedIn => Self != null;

    /// <summary>
    /// The primary node this client is connected to
    /// </summary>
    public Node PrimaryNode { get; set; }

    #region Event Fields

    /// <summary>
    /// Run when the client browser is refocused
    /// TODO: Nothing browser-related should be in SDK.
    /// </summary>
    public event Func<Task> OnRefocus;

    /// <summary>
    /// Run when a channel sends a watching update
    /// </summary>
    public event Func<ChannelWatchingUpdate, Task> OnChannelWatchingUpdate;

    /// <summary>
    /// Run when a channel sends a currently typing update
    /// </summary>
    public event Func<ChannelTypingUpdate, Task> OnChannelCurrentlyTypingUpdate;

    /// <summary>
    /// Run when a personal embed update is received
    /// </summary>
    public event Func<PersonalEmbedUpdate, Task> OnPersonalEmbedUpdate;

    /// <summary>
    /// Run when a channel embed update is received
    /// </summary>
    public event Func<ChannelEmbedUpdate, Task> OnChannelEmbedUpdate;

    /// <summary>
    /// Run when a category is reordered
    /// </summary>
    public event Func<CategoryOrderEvent, Task> OnCategoryOrderUpdate;

#endregion

    public ValourClient(string baseAddress, LoggingService logger = null)
    {
        BaseAddress = baseAddress;
        
        if (logger is null)
            Logger = new LoggingService();
        else 
            Logger = logger;
        
        AuthService = new AuthService(this);
        NodeService = new NodeService(this);
        FriendService = new FriendService(this);
        MessageService = new MessageService(this);
        DirectChannelService = new DirectChannelService(this);
        PlanetService = new PlanetService(this);
        PlanetChannelService = new PlanetChannelService(this);
        ChannelStateService = new ChannelStateService(this);
        BotService = new BotService(this);
        TenorService = new TenorService(this);
        SubscriptionService = new SubscriptionService(this);
        NotificationService = new NotificationService(this);
    }

    /// <summary>
    /// Logs a message to all added loggers
    /// </summary>
    public void Log(string prefix, string message, string color = null)
    {
        Logger.Log(prefix, message, color);
    }

    /// <summary>
    /// Sets the HTTP client
    /// </summary>
    public void SetHttpClient(HttpClient client) => _httpClient = client;

    public void SetupHttpClient()
    {
        _httpClient = new HttpClient()
        {
            BaseAddress = new Uri(BaseAddress)
        };
    }
    
    /// <summary>
    /// Sets the compliance data for the current user
    /// </summary>
    public async ValueTask<TaskResult> SetComplianceDataAsync(DateTime birthDate, Locality locality)
    {
        var result = await PrimaryNode.PostAsync($"api/users/self/compliance/{birthDate.ToString("s")}/{locality}", null);
        var taskResult = new TaskResult()
        {
            Success = result.Success,
            Message = result.Message
        };

        return taskResult;
    }
    
    public async Task<TaskResult<List<EcoAccount>>> GetEcoAccountsAsync()
    {
        return await PrimaryNode.GetJsonAsync<List<EcoAccount>>("api/eco/accounts/self");
    }
    

    public async Task<TaskResult<List<ReferralDataModel>>> GetReferralsAsync()
    {
        return await PrimaryNode.GetJsonAsync<List<ReferralDataModel>>("api/users/self/referrals");
    }

    #region SignalR Events

    public async Task HandleRefocus()
    {
        foreach (var node in NodeManager.Nodes)
        {
            await node.ForceRefresh();
        }
        
        if (OnRefocus is not null)
            await OnRefocus.Invoke();
    }

    public async Task HandleChannelWatchingUpdateRecieved(ChannelWatchingUpdate update)
    {
        //Console.WriteLine("Watching: " + update.ChannelId);
        //foreach (var watcher in update.UserIds)
        //{
        //    Console.WriteLine("- " + watcher);
        //}

        if (OnChannelWatchingUpdate is not null)
            await OnChannelWatchingUpdate.Invoke(update);
    }

    public async Task HandleChannelCurrentlyTypingUpdateRecieved(ChannelTypingUpdate update)
    {
        if (OnChannelCurrentlyTypingUpdate is not null)
            await OnChannelCurrentlyTypingUpdate.Invoke(update);
    }

    public async Task HandlePersonalEmbedUpdate(PersonalEmbedUpdate update)
    {
        if (OnPersonalEmbedUpdate is not null)
            await OnPersonalEmbedUpdate.Invoke(update);
    }

    public async Task HandleChannelEmbedUpdate(ChannelEmbedUpdate update)
    {
        if (OnChannelEmbedUpdate is not null)
            await OnChannelEmbedUpdate.Invoke(update);
    }

    // TODO: change
    public async Task HandleCategoryOrderUpdate(CategoryOrderEvent eventData)
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
    /// Logs in and prepares the client for use
    /// </summary>
    public async Task<TaskResult> InitializeUser(string token)
    {
        // Login to Valour
        var userResult = await AuthService.LoginAsync(token);
        if (!userResult.Success)
            return userResult;
        
        // Setup primary node
        await NodeService.SetupPrimaryNodeAsync();
        
        var loadTasks = new List<Task>()
        {
            // LoadChannelStatesAsync(), this is already done by the Home component
            FriendService.FetchesFriendsAsync(),
            PlanetService.FetchJoinedPlanetsAsync(),
            TenorService.LoadTenorFavoritesAsync(),
            DirectChannelService.LoadDirectChatChannelsAsync(),
            NotificationService.LoadUnreadNotificationsAsync()
        };

        // Load user data concurrently
        await Task.WhenAll(loadTasks);
        
        return TaskResult.SuccessResult;
    }

    #endregion

    public async Task<TaskResult> UpdatePasswordAsync(string oldPassword, string newPassword) {
        var model = new ChangePasswordRequest() { OldPassword = oldPassword, NewPassword = newPassword };
        return await PrimaryNode.PostAsync("api/users/self/password", model);
    }
    
    // Sad zone
    public async Task<TaskResult> DeleteAccountAsync(string password)
    {
        var model = new DeleteAccountModel() { Password = password };
        return await PrimaryNode.PostAsync("api/users/self/hardDelete", model);
    }
}
