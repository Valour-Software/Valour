using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Valour.Sdk.Client;
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
    private static readonly List<Planet> JoinedPlanetsInternal = new();
    
    /// <summary>
    /// Currently opened planets
    /// </summary>
    public static IReadOnlyList<Planet> ConnectedPlanets { get; private set; }
    private static readonly List<Planet> ConnectedPlanetsInternal = new();
    
    /// <summary>
    /// Lookup for opened planets by id
    /// </summary>
    public static IReadOnlyDictionary<long, Planet> ConnectedPlanetsLookup { get; private set; }
    private static readonly Dictionary<long, Planet> ConnectedPlanetsLookupInternal = new();
    
    /// <summary>
    /// A set of locks used to prevent planet connections from closing automatically
    /// </summary>
    public static IReadOnlyDictionary<string, long> PlanetLocks { get; private set; }
    private static readonly Dictionary<string, long> PlanetLocksInternal = new();
    
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
        JoinedPlanets = JoinedPlanetsInternal;
        ConnectedPlanets = ConnectedPlanetsInternal;
        PlanetLocks = PlanetLocksInternal;
        ConnectedPlanetsLookup = ConnectedPlanetsLookupInternal;
        
        // Setup reconnect logic
        NodeService.NodeReconnected += OnNodeReconnect;
    }
    
    /// <summary>
    /// Fetches all planets that the user has joined from the server
    /// </summary>
    public static async Task<TaskResult> FetchJoinedPlanetsAsync()
    {
        var response = await ValourClient.PrimaryNode.GetJsonAsync<List<Planet>>($"api/users/self/planets");
        if (!response.Success)
            return response.WithoutData();

        var planets = response.Data;

        JoinedPlanetsInternal.Clear();
        
        // Add to cache
        foreach (var planet in planets)
        {
            JoinedPlanetsInternal.Add(planet.Sync());
        }
        
        JoinedPlanetsUpdate?.Invoke();
        
        return TaskResult.SuccessResult;
    }
    
    /// <summary>
    /// Returns if the given planet is open
    /// </summary>
    public static bool IsPlanetConnected(Planet planet) =>
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
            PlanetLocksInternal[key] = planet.Id;
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
        ConnectedPlanetsInternal.Add(planet);
        ConnectedPlanetsLookupInternal[planet.Id] = planet;

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
            var lockResult = RemovePlanetLock(key);
            if (lockResult == ConnectionLockResult.Locked)
            {
                return TaskResult.FromFailure("Planet is locked by other keys.");
            }
            // If for some reason our key isn't actually there
            // (shouldn't happen, but just in case)
            else if (lockResult == ConnectionLockResult.NotFound)
            {
                if (PlanetLocksInternal.Values.Any(x => x == planet.Id))
                {
                    return TaskResult.FromFailure("Planet is locked by other keys.");
                }
            }
        }
        
        // Already closed
        if (!ConnectedPlanets.Contains(planet))
            return TaskResult.SuccessResult;

        // Close connection
        await planet.Node.HubConnection.SendAsync("LeavePlanet", planet.Id);

        // Remove from list
        ConnectedPlanetsInternal.Remove(planet);
        ConnectedPlanetsLookupInternal.Remove(planet.Id);

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
        PlanetLocksInternal[key] = planetId;
        
        Console.WriteLine("Planet lock added.");
        Console.WriteLine(JsonSerializer.Serialize(PlanetLocks));
    }

    /// <summary>
    /// Removes the lock for a planet.
    /// Returns if there are any locks left for the planet.
    /// </summary>
    private static ConnectionLockResult RemovePlanetLock(string key)
    {
        if (PlanetLocksInternal.TryGetValue(key, out var planetId))
        {
            Console.WriteLine($"Planet lock {key} removed.");
            PlanetLocksInternal.Remove(key);
            return PlanetLocksInternal.Any(x => x.Value == planetId) ? 
                ConnectionLockResult.Locked : 
                ConnectionLockResult.Unlocked;
        }
        
        return ConnectionLockResult.NotFound;
    }
    
    /// <summary>
    /// Adds a planet to the joined planets list and invokes the event.
    /// </summary>
    public static void AddJoinedPlanet(Planet planet)
    {
        JoinedPlanetsInternal.Add(planet);

        PlanetJoined?.Invoke(planet);
        JoinedPlanetsUpdate?.Invoke();
    }
    
    /// <summary>
    /// Removes a planet from the joined planets list and invokes the event.
    /// </summary>
    public static void RemoveJoinedPlanet(Planet planet)
    {
        JoinedPlanetsInternal.Remove(planet);
        
        PlanetLeft?.Invoke(planet);
        JoinedPlanetsUpdate?.Invoke();
    }
    
    /// <summary>
    /// Attempts to join the given planet
    /// </summary>
    public static async Task<TaskResult<PlanetMember>> JoinPlanetAsync(Planet planet)
    {
        var result = await ValourClient.PrimaryNode.PostAsyncWithResponse<PlanetMember>($"api/planets/{planet.Id}/discover");

        if (result.Success)
        {
            AddJoinedPlanet(planet);
        }

        return result;
    }

    /// <summary>
    /// Attempts to leave the given planet
    /// </summary>
    public static async Task<TaskResult> LeavePlanetAsync(Planet planet)
    {
        // Get member
        var member = await planet.GetSelfMemberAsync();
        var result = await member.DeleteAsync();

        if (result.Success)
            RemoveJoinedPlanet(planet);

        return result;
    }

    public static void SetJoinedPlanets(List<Planet> planets)
    {
        JoinedPlanetsInternal.Clear();
        JoinedPlanetsInternal.AddRange(planets);
        
        JoinedPlanetsUpdate?.Invoke();
    }

    public static async Task OnNodeReconnect(Node node)
    {
        foreach (var planet in ConnectedPlanetsInternal.Where(x => x.NodeName == node.Name))
        {
            await node.HubConnection.SendAsync("JoinPlanet", planet.Id);
            await node.Log($"Rejoined SignalR group for planet {planet.Id}", "lime");
        }
    }
    
}