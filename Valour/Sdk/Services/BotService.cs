using Valour.Sdk.Client;
using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Sdk.Services;

/// <summary>
/// Provides functionality for running Valour headless bots and managing bot accounts
/// </summary>
public class BotService : ServiceBase
{
    private readonly LogOptions _logOptions = new(
        "BotService",
        "#3381a3",
        "#a3333e",
        "#a39433"
    );

    private readonly ValourClient _client;

    public BotService(ValourClient client)
    {
        _client = client;
        SetupLogging(client.Logger, _logOptions);
    }

    #region Bot Management

    /// <summary>
    /// Gets all bots owned by the current user
    /// </summary>
    public async Task<List<BotResponse>> GetMyBotsAsync()
    {
        var response = await _client.PrimaryNode.GetJsonAsync<List<BotResponse>>("api/bots");
        if (!response.Success)
        {
            LogError($"Failed to get bots: {response.Message}");
            return new List<BotResponse>();
        }
        return response.Data ?? new List<BotResponse>();
    }

    /// <summary>
    /// Creates a new bot account
    /// </summary>
    public async Task<TaskResult<BotResponse>> CreateBotAsync(string name)
    {
        var request = new CreateBotRequest { Name = name };
        var response = await _client.PrimaryNode.PostAsyncWithResponse<BotResponse>("api/bots", request);

        if (!response.Success)
        {
            LogError($"Failed to create bot: {response.Message}");
            return new TaskResult<BotResponse>(false, response.Message);
        }

        return new TaskResult<BotResponse>(true, "Bot created successfully", response.Data);
    }

    /// <summary>
    /// Updates a bot's information
    /// </summary>
    public async Task<TaskResult<BotResponse>> UpdateBotAsync(long botId, UpdateBotRequest request)
    {
        var response = await _client.PrimaryNode.PutAsyncWithResponse<BotResponse>($"api/bots/{botId}", request);

        if (!response.Success)
        {
            LogError($"Failed to update bot: {response.Message}");
            return new TaskResult<BotResponse>(false, response.Message);
        }

        return new TaskResult<BotResponse>(true, "Bot updated successfully", response.Data);
    }

    /// <summary>
    /// Deletes a bot account
    /// </summary>
    public async Task<TaskResult> DeleteBotAsync(long botId)
    {
        var response = await _client.PrimaryNode.DeleteAsync($"api/bots/{botId}");

        if (!response.Success)
        {
            LogError($"Failed to delete bot: {response.Message}");
            return new TaskResult(false, response.Message);
        }

        return new TaskResult(true, "Bot deleted successfully");
    }

    /// <summary>
    /// Regenerates a bot's token
    /// </summary>
    public async Task<TaskResult<BotResponse>> RegenerateBotTokenAsync(long botId)
    {
        var response = await _client.PrimaryNode.PostAsyncWithResponse<BotResponse>($"api/bots/{botId}/token/regenerate", null);

        if (!response.Success)
        {
            LogError($"Failed to regenerate token: {response.Message}");
            return new TaskResult<BotResponse>(false, response.Message);
        }

        return new TaskResult<BotResponse>(true, "Token regenerated successfully", response.Data);
    }

    #endregion

    #region Bot Runtime
    
    /// <summary>
    /// Logs in and prepares the bot's client for use
    /// </summary>
    public async Task<AuthResult> InitializeBot(string email, string password)
    {
        // Ensure client HTTP client is set
        _client.SetupHttpClient();

        // Login to account
        var userResult = await _client.AuthService.LoginAsync(email, password);
        if (!userResult.Success)
            return userResult;
        
        // Now that we have our user, we can set up our primary node
        await _client.NodeService.SetupPrimaryNodeAsync();

        Console.WriteLine($"Initialized bot {_client.Me.Name} ({_client.Me.Id})");

        await JoinAllChannelsAsync();

        return new AuthResult()
        {
            Success = true,
            Message = "Success"
        };
    }
    
    /// <summary>
    /// Should only be run during initialization of bots!
    /// </summary>
    public async Task JoinAllChannelsAsync()
    {
        // Get all joined planets
        var planets = (await _client.PrimaryNode.GetJsonAsync<List<Planet>>("api/users/me/planets")).Data;

        var planetTasks = new List<Task>();
        
        // Add to cache
        foreach (var planet in planets)
        {
            var cached = planet.Sync(_client);
            await planet.FetchChannelsAsync();
            
            planetTasks.Add(Task.Run(async () =>
            {
                await _client.PlanetService.TryOpenPlanetConnection(planet, "bot-init");
                
                var channelTasks = new List<Task>();
                
                foreach (var channel in planet.Channels)
                {
                    channelTasks.Add(_client.ChannelService.TryOpenPlanetChannelConnection(channel, "bot-init"));
                }
                
                await Task.WhenAll(channelTasks);
            }));
        }

        await Task.WhenAll(planetTasks);

        _client.PlanetService.SetJoinedPlanets(planets);
    }

    #endregion
}