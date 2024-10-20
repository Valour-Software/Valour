using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Valour.Sdk.Nodes;
using Valour.Shared;
using Valour.Shared.Utilities;

namespace Valour.SDK.Services;

public static class PlanetService
{
    /// <summary>
    /// Run when a planet connection opens
    /// </summary>
    public static HybridEvent<Planet> PlanetConnected;

    /// <summary>
    /// Run when SignalR closes a planet
    /// </summary>
    public static HybridEvent<Planet> PlanetDisconnected;

    /// <summary>
    /// Run when a planet is joined
    /// </summary>
    public static HybridEvent<Planet> PlanetJoined;
    
    /// <summary>
    /// Run when the joined planets list is updated
    /// </summary>
    public static HybridEvent JoinedPlanetsUpdate;

    /// <summary>
    /// Run when a planet is left
    /// </summary>
    public static HybridEvent<Planet> PlanetLeft;
    
    /// <summary>
    /// The planets this client has joined
    /// </summary>
    public static IReadOnlyList<Planet> JoinedPlanets { get; private set; }
    private static List<Planet> _joinedPlanets = new();
    
    /// <summary>
    /// Currently opened planets
    /// </summary>
    public static IReadOnlyList<Planet> ConnectedPlanets { get; private set; }
    private static List<Planet> _connectedPlanets = new();
    
    /// <summary>
    /// Lookup for opened planets by id
    /// </summary>
    public static IReadOnlyDictionary<long, Planet> ConnectedPlanetsLookup { get; private set; }
    private static Dictionary<long, Planet> _connectedPlanetsLookup = new();
    
    /// <summary>
    /// A set of locks used to prevent planet connections from closing automatically
    /// </summary>
    public static IReadOnlyDictionary<string, long> PlanetLocks { get; private set; }
    private static Dictionary<string, long> _planetLocks = new();
    
    static PlanetService()
    {
        // Add victor dummy member
        PlanetMember.Cache.PutReplace(long.MaxValue, new PlanetMember()
        {
            Nickname = "Victor",
            Id = long.MaxValue,
            MemberAvatar = "/media/victor-cyan.png"
        });
        
        // Setup readonly collections
        JoinedPlanets = _joinedPlanets;
        ConnectedPlanets = _connectedPlanets;
        PlanetLocks = _planetLocks;
        ConnectedPlanetsLookup = _connectedPlanetsLookup;
    }
    
    /// <summary>
    /// Returns if the given planet is open
    /// </summary>
    public static bool IsPlanetOpen(Planet planet) =>
        ConnectedPlanets.Any(x => x.Id == planet.Id);
    
    /// <summary>
    /// Opens a planet and prepares it for use
    /// </summary>
    public static async Task<TaskResult> TryOpenPlanetConnection(Planet planet, string key)
    {
        // Cannot open null
        if (planet is null)
            return TaskResult.FromFailure("Planet is null");

        if (PlanetLocks.ContainsKey(key))
        {
            _planetLocks[key] = planet.Id;
        }
        else
        {
            // Add lock
            AddPlanetLock(key, planet.Id);
        }

        // Already open
        if (ConnectedPlanets.Contains(planet))
            return TaskResult.SuccessResult;

        // Mark as opened
        _connectedPlanets.Add(planet);
        _connectedPlanetsLookup[planet.Id] = planet;

        Console.WriteLine($"Opening planet {planet.Name} ({planet.Id})");

        var sw = new Stopwatch();

        sw.Start();

        // Get node for planet
        var node = await NodeManager.GetNodeForPlanetAsync(planet.Id);

        List<Task> tasks = new();

        // Joins SignalR group
        var result = await node.HubConnection.InvokeAsync<TaskResult>("JoinPlanet", planet.Id);
        Console.WriteLine(result.Message);

        if (!result.Success)
            return result;

        // Load roles early for cached speed
        await planet.LoadRolesAsync();

        // Load member data early for the same reason (speed)
        tasks.Add(planet.FetchMemberDataAsync());

        // Load channels
        tasks.Add(planet.FetchChannelsAsync());
        
        // Load permissions nodes
        tasks.Add(planet.FetchPermissionsNodesAsync());

        // requesting/loading the data does not require data from other requests/types
        // so just await them all, instead of one by one
        await Task.WhenAll(tasks);

        sw.Stop();

        Console.WriteLine($"Time to open this Planet: {sw.ElapsedMilliseconds}ms");

        // Log success
        Console.WriteLine($"Joined SignalR group for planet {planet.Name} ({planet.Id})");

        PlanetConnected?.Invoke(planet);
        
        return TaskResult.SuccessResult;
    }
    
    /// <summary>
    /// Closes a SignalR connection to a planet
    /// </summary>
    public static async Task<TaskResult> TryClosePlanetConnection(Planet planet, string key, bool force = false)
    {
        if (!force)
        {
            var locked = RemovePlanetLock(key);
            if (locked)
                return TaskResult.FromFailure("Planet is locked by other keys.");
        }
        
        // Already closed
        if (!ConnectedPlanets.Contains(planet))
            return TaskResult.SuccessResult;

        // Close connection
        await planet.Node.HubConnection.SendAsync("LeavePlanet", planet.Id);

        // Remove from list
        _connectedPlanets.Remove(planet);
        _connectedPlanetsLookup.Remove(planet.Id);

        Console.WriteLine($"Left SignalR group for planet {planet.Name} ({planet.Id})");

        // Invoke event
        PlanetDisconnected?.Invoke(planet);
        
        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// Prevents a planet from closing connections automatically.
    /// Key is used to allow multiple locks per planet.
    /// </summary>
    private static void AddPlanetLock(string key, long planetId)
    {
        _planetLocks[key] = planetId;
        
        Console.WriteLine("Planet lock added.");
        Console.WriteLine(JsonSerializer.Serialize(PlanetLocks));
    }

    /// <summary>
    /// Removes the lock for a planet.
    /// Returns if there are any locks left for the planet.
    /// </summary>
    private static bool RemovePlanetLock(string key)
    {
        var found = PlanetLocks.TryGetValue(key, out var planetId);

        if (found)
        {
            _planetLocks.Remove(key);
        }
        
        Console.WriteLine("Planet lock removed.");
        Console.WriteLine(JsonSerializer.Serialize(PlanetLocks));

        return PlanetLocks.Any(x => x.Value == planetId);
    }
    
}