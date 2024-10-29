using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Valour.Sdk.Client;
using Valour.Sdk.Nodes;
using Valour.Shared;
using Valour.Shared.Utilities;

namespace Valour.SDK.Services;

public class PlanetService : ServiceBase
{
    /// <summary>
    /// Run when a planet connection opens
    /// </summary>
    public HybridEvent<Planet> PlanetConnected;

    /// <summary>
    /// Run when SignalR closes a planet
    /// </summary>
    public HybridEvent<Planet> PlanetDisconnected;

    /// <summary>
    /// Run when a planet is joined
    /// </summary>
    public HybridEvent<Planet> PlanetJoined;
    
    /// <summary>
    /// Run when the joined planets list is updated
    /// </summary>
    public HybridEvent JoinedPlanetsUpdate;

    /// <summary>
    /// Run when a planet is left
    /// </summary>
    public HybridEvent<Planet> PlanetLeft;

    /// <summary>
    /// The planets this client has joined
    /// </summary>
    public readonly IReadOnlyList<Planet> JoinedPlanets;
    private readonly List<Planet> _joinedPlanets = new();

    /// <summary>
    /// Currently opened planets
    /// </summary>
    public readonly IReadOnlyList<Planet> ConnectedPlanets;
    private readonly List<Planet> _connectedPlanets = new();

    /// <summary>
    /// Lookup for opened planets by id
    /// </summary>
    public readonly IReadOnlyDictionary<long, Planet> ConnectedPlanetsLookup;
    private readonly Dictionary<long, Planet> _connectedPlanetsLookup = new();

    /// <summary>
    /// A set of locks used to prevent planet connections from closing automatically
    /// </summary>
    public readonly IReadOnlyDictionary<string, long> PlanetLocks;
    private readonly Dictionary<string, long> _planetLocks = new();

    private readonly LogOptions _logOptions = new(
        "PlanetService",
        "#3381a3",
        "#a3333e",
        "#a39433"
    );
    
    private readonly ValourClient _client;
    
    public PlanetService(ValourClient client)
    {
        _client = client;
        
        // Add victor dummy member
        _client.Cache.PlanetMembers.PutReplace(long.MaxValue, new PlanetMember()
        {
            Nickname = "Victor",
            Id = long.MaxValue,
            MemberAvatar = "./_content/Valour.Client/media/logo/logo-256.webp"
        });
        
        // Setup readonly collections
        JoinedPlanets = _joinedPlanets;
        ConnectedPlanets = _connectedPlanets;
        PlanetLocks = _planetLocks;
        ConnectedPlanetsLookup = _connectedPlanetsLookup;
        
        // Setup logging
        SetupLogging(client.Logger, _logOptions);
        
        // Setup reconnect logic
        _client.NodeService.NodeReconnected += OnNodeReconnect;
    }
    
    /// <summary>
    /// Retrieves and returns a client planet by requesting from the server
    /// </summary>
    public async ValueTask<Planet> FetchPlanetAsync(long id, bool skipCache = false)
    {
        if (!skipCache && _client.Cache.Planets.TryGet(id, out var cached))
            return cached;
        
        var planet = (await _client.PrimaryNode.GetJsonAsync<Planet>($"api/planets/{id}")).Data;
        
        return _client.Cache.Sync(planet);
    }
    
    /// <summary>
    /// Fetches all planets that the user has joined from the server
    /// </summary>
    public async Task<TaskResult> FetchJoinedPlanetsAsync()
    {
        var response = await _client.PrimaryNode.GetJsonAsync<List<Planet>>($"api/users/self/planets");
        if (!response.Success)
            return response.WithoutData();

        var planets = response.Data;

        _joinedPlanets.Clear();
        
        // Add to cache
        foreach (var planet in planets)
        {
            _joinedPlanets.Add(_client.Cache.Sync(planet));
        }
        
        JoinedPlanetsUpdate?.Invoke();
        
        return TaskResult.SuccessResult;
    }
    
    /// <summary>
    /// Returns if the given planet is open
    /// </summary>
    public bool IsPlanetConnected(Planet planet) =>
        ConnectedPlanets.Any(x => x.Id == planet.Id);
    
    /// <summary>
    /// Opens a planet and prepares it for use
    /// </summary>
    public async Task<TaskResult> TryOpenPlanetConnection(Planet planet, string key)
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

        Log($"Opening planet {planet.Name} ({planet.Id})");

        var sw = new Stopwatch();

        sw.Start();

        // Get node for planet
        var node = await _client.NodeService.GetNodeForPlanetAsync(planet.Id);

        List<Task> tasks = new();

        // Joins SignalR group
        var result = await node.HubConnection.InvokeAsync<TaskResult>("JoinPlanet", planet.Id);

        if (!result.Success)
        {
            LogError(result.Message);
            return result;
        }
        
        Log(result.Message);

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

        Log($"Time to open this Planet: {sw.ElapsedMilliseconds}ms");

        // Log success
        Log($"Joined SignalR group for planet {planet.Name} ({planet.Id})");

        PlanetConnected?.Invoke(planet);
        
        return TaskResult.SuccessResult;
    }
    
    /// <summary>
    /// Closes a SignalR connection to a planet
    /// </summary>
    public async Task<TaskResult> TryClosePlanetConnection(Planet planet, string key, bool force = false)
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
                if (_planetLocks.Values.Any(x => x == planet.Id))
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
        _connectedPlanets.Remove(planet);
        _connectedPlanetsLookup.Remove(planet.Id);

        Log($"Left SignalR group for planet {planet.Name} ({planet.Id})");

        // Invoke event
        PlanetDisconnected?.Invoke(planet);
        
        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// Prevents a planet from closing connections automatically.
    /// Key is used to allow multiple locks per planet.
    /// </summary>
    private void AddPlanetLock(string key, long planetId)
    {
        _planetLocks[key] = planetId;
        
        Log($"Planet lock {key} added for {planetId}");
    }

    /// <summary>
    /// Removes the lock for a planet.
    /// Returns if there are any locks left for the planet.
    /// </summary>
    private ConnectionLockResult RemovePlanetLock(string key)
    {
        if (_planetLocks.TryGetValue(key, out var planetId))
        {
            Log($"Planet lock {key} removed for {planetId}");
            _planetLocks.Remove(key);
            return _planetLocks.Any(x => x.Value == planetId) ? 
                ConnectionLockResult.Locked : 
                ConnectionLockResult.Unlocked;
        }
        
        return ConnectionLockResult.NotFound;
    }
    
    /// <summary>
    /// Adds a planet to the joined planets list and invokes the event.
    /// </summary>
    public void AddJoinedPlanet(Planet planet)
    {
        _joinedPlanets.Add(planet);
        PlanetJoined?.Invoke(planet);
        JoinedPlanetsUpdate?.Invoke();
    }
    
    /// <summary>
    /// Removes a planet from the joined planets list and invokes the event.
    /// </summary>
    public void RemoveJoinedPlanet(Planet planet)
    {
        _joinedPlanets.Remove(planet);
        
        PlanetLeft?.Invoke(planet);
        JoinedPlanetsUpdate?.Invoke();
    }
    
    /// <summary>
    /// Attempts to join the given planet
    /// </summary>
    public async Task<TaskResult<PlanetMember>> JoinPlanetAsync(Planet planet)
    {
        var result = await _client.PrimaryNode.PostAsyncWithResponse<PlanetMember>($"api/planets/{planet.Id}/discover");

        if (result.Success)
        {
            AddJoinedPlanet(planet);
        }

        return result;
    }

    /// <summary>
    /// Attempts to leave the given planet
    /// </summary>
    public async Task<TaskResult> LeavePlanetAsync(Planet planet)
    {
        // Get member
        var member = await planet.GetSelfMemberAsync();
        var result = await member.DeleteAsync();

        if (result.Success)
            RemoveJoinedPlanet(planet);

        return result;
    }

    public void SetJoinedPlanets(List<Planet> planets)
    {
        _joinedPlanets.Clear();
        _joinedPlanets.AddRange(planets);
        JoinedPlanetsUpdate?.Invoke();
    }
    
    public async Task<List<Planet>> FetchDiscoverablePlanetsAsync()
    {
        var response = await _client.PrimaryNode.GetJsonAsync<List<Planet>>($"api/planets/discoverable");
        if (!response.Success)
            return new List<Planet>();

        var planets = response.Data;
        
        var result = new List<Planet>();

        foreach (var planet in planets)
            result.Add(_client.Cache.Sync(planet));

        return result;
    }

    private async Task OnNodeReconnect(Node node)
    {
        foreach (var planet in _connectedPlanets.Where(x => x.NodeName == node.Name))
        {
            await node.HubConnection.SendAsync("JoinPlanet", planet.Id);
            Log($"Rejoined SignalR group for planet {planet.Id}");
        }
    }
    
}