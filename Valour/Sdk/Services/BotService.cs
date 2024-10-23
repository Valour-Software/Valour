using System.Net;
using Valour.Sdk.Client;
using Valour.Sdk.Nodes;
using Valour.Shared;

namespace Valour.SDK.Services;

/// <summary>
/// Provides functionality for running Valour headless bots
/// </summary>
public static class BotService
{
    /// <summary>
    /// Logs in and prepares the bot's client for use
    /// </summary>
    public static async Task<TaskResult> InitializeBot(string email, string password, HttpClient http = null)
    {
        // Ensure client HTTP client is set
        ValourClient.SetupHttpClient();

        // Login to account
        var userResult = await AuthService.LoginAsync(email, password);
        if (!userResult.Success)
            return userResult;
        
        // Now that we have our user, we can set up our primary node
        await NodeService.SetupPrimaryNodeAsync();

        Console.WriteLine($"Initialized bot {ValourClient.Self.Name} ({ValourClient.Self.Id})");

        await JoinAllChannelsAsync();

        return new TaskResult(true, "Success");
    }


    
    /// <summary>
    /// Should only be run during initialization of bots!
    /// </summary>
    public static async Task JoinAllChannelsAsync()
    {
        // Get all joined planets
        var planets = (await ValourClient.PrimaryNode.GetJsonAsync<List<Planet>>("api/users/self/planets")).Data;

        var planetTasks = new List<Task>();
        
        // Add to cache
        foreach (var planet in planets)
        {
            var cached = planet.Sync();
            await planet.FetchChannelsAsync();
            
            planetTasks.Add(Task.Run(async () =>
            {
                await PlanetService.TryOpenPlanetConnection(planet, "bot-init");
                
                var channelTasks = new List<Task>();
                
                foreach (var channel in planet.Channels)
                {
                    channelTasks.Add(PlanetChannelService.TryOpenPlanetChannelConnection(channel, "bot-init"));
                }
                
                await Task.WhenAll(channelTasks);
            }));
        }

        await Task.WhenAll(planetTasks);

        PlanetService.SetJoinedPlanets(planets);
    }
}