using Valour.Sdk.Client;
using Valour.Shared;

namespace Valour.Sdk.Services;

/// <summary>
/// Provides functionality for running Valour headless bots
/// </summary>
public class BotService : ServiceBase
{
    private readonly ValourClient _client;
    
    public BotService(ValourClient client)
    {
        _client = client;
        SetupLogging(client.Logger);
    }
    
    /// <summary>
    /// Logs in and prepares the bot's client for use
    /// </summary>
    public async Task<TaskResult> InitializeBot(string email, string password)
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

        return new TaskResult(true, "Success");
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
            var cached = _client.Cache.Sync(planet);
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
}