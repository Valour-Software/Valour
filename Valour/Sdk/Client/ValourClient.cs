using Valour.Sdk.Nodes;
using Valour.Sdk.Services;
using Valour.Sdk.Utility;
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
    public readonly UserService UserService;
    public readonly ChannelStateService ChannelStateService;
    public readonly FriendService FriendService;
    public readonly MessageService MessageService;
    public readonly NodeService NodeService;
    public readonly PlanetService PlanetService;
    public readonly ChannelService  ChannelService;
    public readonly PermissionService PermissionService;
    public readonly TenorService TenorService;
    public readonly SubscriptionService SubscriptionService;
    public readonly NotificationService NotificationService;
    public readonly AutomodService AutomodService;
    public readonly EcoService EcoService;
    public readonly StaffService StaffService;
    public readonly OauthService OauthService;
    public readonly OauthHelper OauthHelper;
    public readonly SafetyService SafetyService;
    public readonly ThemeService ThemeService;
    public readonly UnreadService UnreadService;
    public readonly PlanetTagService PlanetTagService;

    /// <summary>
    /// The base address the client is connected to
    /// </summary>
    public string BaseAddress { get; private set; }

    /// <summary>
    /// The user for this client instance
    /// </summary>
    public User Me { get; set; }

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
    public bool IsLoggedIn => Me != null;

    /// <summary>
    /// The primary node this client is connected to
    /// </summary>
    public Node PrimaryNode { get; set; }
    
    /// <summary>
    /// Used mostly for testing, allows for a custom HttpClientProvider
    /// </summary>
    public HttpClientProvider HttpClientProvider { get; set; }
    

    public ValourClient(string baseAddress, LoggingService logger = null, HttpClientProvider httpProvider = null)
    {
        BaseAddress = baseAddress;
        
        if (!BaseAddress.EndsWith('/'))
            BaseAddress += '/';
        
        if (logger is null)
            Logger = new LoggingService();
        else 
            Logger = logger;
        
        HttpClientProvider = httpProvider;
        
        Logger.Log("App", $"ValourClient Base address: {BaseAddress}", "magenta");
        
        Cache = new CacheService(this);
        AuthService = new AuthService(this);
        NodeService = new NodeService(this);
        UserService = new UserService(this);
        FriendService = new FriendService(this);
        MessageService = new MessageService(this);
        PlanetService = new PlanetService(this);
        ChannelService = new ChannelService(this);
        ChannelStateService = new ChannelStateService(this);
        PermissionService = new PermissionService(this);
        BotService = new BotService(this);
        SubscriptionService = new SubscriptionService(this);
        NotificationService = new NotificationService(this);
        AutomodService = new AutomodService(this);
        EcoService = new EcoService(this);
        StaffService = new StaffService(this);
        OauthService = new OauthService(this);
        OauthHelper = new OauthHelper(this);
        SafetyService = new SafetyService(this);
        ThemeService = new ThemeService(this);
        UnreadService = new UnreadService(this);
        PlanetTagService = new PlanetTagService(this);

        var tenorHttpClient = new HttpClient();
        tenorHttpClient.BaseAddress = new Uri("https://tenor.googleapis.com/v2/");
        TenorService = new TenorService(tenorHttpClient, this);
    }
    
    /// <summary>
    /// Sets the origin of the client. Should only be called
    /// before nodes are initialized. Origins must NOT end in a slash.
    /// </summary>
    public void SetOrigin(string origin)
    {
        // Cloudflare pages. Use main api endpoint.
        if (origin.Contains(".valour.pages.dev"))
            origin = "https://app.valour.gg";
        
        BaseAddress = origin;
        _httpClient = new HttpClient()
        {
            BaseAddress = new Uri(BaseAddress)
        };
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
    public void SetHttpClient(HttpClient client)
    {
        _httpClient = client;
    }

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
    public async Task<TaskResult> InitializeUser(string token = null)
    {
        if (token is not null)
            AuthService.SetToken(token);
        
        // Login to Valour
        var userResult = await AuthService.LoginAsync();
        if (!userResult.Success)
            return userResult;
        
        // Setup primary node
        // await NodeService.SetupPrimaryNodeAsync(); Already done in LoginAsync
        
        var loadTasks = new List<Task>()
        {
            // LoadChannelStatesAsync(), this is already done by the Home component
            FriendService.FetchFriendsAsync(),
            PlanetService.FetchJoinedPlanetsAsync(),
            TenorService.LoadTenorFavoritesAsync(),
            ChannelService.LoadDmChannelsAsync(),
            NotificationService.LoadUnreadNotificationsAsync()
        };

        // Load user data concurrently
        try
        {
            await Task.WhenAll(loadTasks);
        } 
        catch (Exception e)
        {
            Logger.Log("App", "Critical error during startup: " + e.Message, "red");
            return new TaskResult(false, "Critical error during startup: " + e.Message);
        }

        return TaskResult.SuccessResult;
    }
        
    public async Task<TaskResult<List<ReferralDataModel>>> GetMyReferralsAsync()
    {
        return await PrimaryNode.GetJsonAsync<List<ReferralDataModel>>("api/users/me/referrals");
    }
    
    public async Task<TaskResult> UpdateMyPasswordAsync(string oldPassword, string newPassword) {
        var model = new ChangePasswordRequest() { OldPassword = oldPassword, NewPassword = newPassword };
        return await PrimaryNode.PostAsync("api/users/me/password", model);
    }

    public async Task<TaskResult> UpdateMyUsernameAsync(string newUsername, string password)
    {
        var model = new ChangeUsernameRequest() { NewUsername = newUsername, Password = password };
        var result =  await PrimaryNode.PostAsync("api/users/me/username", model);

        if (result.Success)
            Me.Name = newUsername;
        
        return result;
    }
    
    // Sad zone
    public async Task<TaskResult> DeleteMyAccountAsync(string password)
    {
        var model = new DeleteAccountModel() { Password = password };
        return await PrimaryNode.PostAsync("api/users/me/hardDelete", model);
    }
}
