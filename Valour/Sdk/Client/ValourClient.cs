using Valour.Sdk.Nodes;
using Valour.Sdk.Services;
using Valour.Shared;
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

    public readonly LoggingService Logger;
    public readonly CacheService Cache;
    public readonly BotService BotService;
    public readonly AuthService AuthService;
    public readonly ChannelStateService ChannelStateService;
    public readonly FriendService FriendService;
    public readonly MessageService MessageService;
    public readonly NodeService NodeService;
    public readonly PlanetService PlanetService;
    public readonly ChannelService ChannelService;
    public readonly PermissionService PermissionService;
    public readonly TenorService TenorService;
    public readonly SubscriptionService SubscriptionService;
    public readonly NotificationService NotificationService;
    public readonly EcoService EcoService;
    public readonly StaffService StaffService;

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

    public ValourClient(string baseAddress, LoggingService logger = null)
    {
        BaseAddress = baseAddress;
        
        if (logger is null)
            Logger = new LoggingService();
        else 
            Logger = logger;
        
        Cache = new CacheService(this);
        AuthService = new AuthService(this);
        NodeService = new NodeService(this);
        FriendService = new FriendService(this);
        MessageService = new MessageService(this);
        PlanetService = new PlanetService(this);
        ChannelService = new ChannelService(this);
        ChannelStateService = new ChannelStateService(this);
        PermissionService = new PermissionService(this);
        BotService = new BotService(this);
        TenorService = new TenorService(this);
        SubscriptionService = new SubscriptionService(this);
        NotificationService = new NotificationService(this);
        EcoService = new EcoService(this);
        StaffService = new StaffService(this);
    }
    
    /// <summary>
    /// Sets the origin of the client. Should only be called
    /// before nodes are initialized. Origins must NOT end in a slash.
    /// </summary>
    public void SetOrigin(string origin)
    {
        BaseAddress = origin;
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
    /// Logs in and prepares the client for use
    /// </summary>
    public async Task<TaskResult> InitializeUser(string token)
    {
        AuthService.SetToken(token);
        
        // Login to Valour
        var userResult = await AuthService.LoginAsync();
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
            ChannelService.LoadDmChannelsAsync(),
            NotificationService.LoadUnreadNotificationsAsync()
        };

        // Load user data concurrently
        await Task.WhenAll(loadTasks);
        
        return TaskResult.SuccessResult;
    }
        
    public async Task<TaskResult<List<ReferralDataModel>>> GetSelfReferralsAsync()
    {
        return await PrimaryNode.GetJsonAsync<List<ReferralDataModel>>("api/users/self/referrals");
    }
    
    public async Task<TaskResult> UpdateSelfPasswordAsync(string oldPassword, string newPassword) {
        var model = new ChangePasswordRequest() { OldPassword = oldPassword, NewPassword = newPassword };
        return await PrimaryNode.PostAsync("api/users/self/password", model);
    }
    
    // Sad zone
    public async Task<TaskResult> DeleteSelfAccountAsync(string password)
    {
        var model = new DeleteAccountModel() { Password = password };
        return await PrimaryNode.PostAsync("api/users/self/hardDelete", model);
    }
}
