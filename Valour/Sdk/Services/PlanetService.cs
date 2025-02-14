using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Valour.Sdk.Client;
using Valour.Sdk.ModelLogic;
using Valour.Sdk.Nodes;
using Valour.Shared;
using Valour.Shared.Models;
using Valour.Shared.Utilities;

namespace Valour.Sdk.Services;

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
    /// Run when the list of connected planets is updated
    /// </summary>
    public HybridEvent ConnectedPlanetsUpdated;

    /// <summary>
    /// Run when a planet is joined
    /// </summary>
    public HybridEvent<Planet> PlanetJoined;

    /// <summary>
    /// Run when the joined planets list is updated
    /// </summary>
    public HybridEvent JoinedPlanetsUpdated;

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
        _client.NodeService.NodeAdded += HookHubEvents;
    }

    /// <summary>
    /// Retrieves and returns a client planet by requesting from the server
    /// </summary>
    public async ValueTask<Planet> FetchPlanetAsync(long id, bool skipCache = false)
    {
        if (!skipCache && _client.Cache.Planets.TryGet(id, out var cached))
            return cached;

        var planet = (await _client.PrimaryNode.GetJsonAsync<Planet>($"api/planets/{id}")).Data;

        await planet.EnsureReadyAsync();

        return planet.Sync(_client);
    }

    /// <summary>
    /// Fetches initial data for planet setup
    /// </summary>
    public async Task<TaskResult<InitialPlanetData>> FetchInitialPlanetDataAsync(long planetId)
    {
        var planet = await FetchPlanetAsync(planetId);
        return await FetchInitialPlanetDataAsync(planet);
    }
    
    /// <summary>
    /// Fetches initial data for planet setup
    /// </summary>
    public async Task<TaskResult> FetchInitialPlanetDataAsync(Planet planet)
    {
        var result = await planet.Node.GetJsonAsync<InitialPlanetData>($"api/planets/{planet.Id}/initialData");
        if (result.Success)
        {
            var data = result.Data;
            data.Roles.SyncAll(_client);
            data.Channels.SyncAll(_client);
        }
        
        return result.WithoutData();
    }

    /// <summary>
    /// Returns the invite for the given invite code (id)
    /// </summary>
    public async Task<PlanetInvite> FetchInviteAsync(string code, bool skipCache = false)
    {
        if (_client.Cache.PlanetInvites.TryGet(code, out var cached))
            return cached;

        var invite = (await _client.PrimaryNode.GetJsonAsync<PlanetInvite>(ISharedPlanetInvite.GetIdRoute(code))).Data;

        return _client.Cache.Sync(invite);
    }

    public async Task<InviteScreenModel> FetchInviteScreenData(string code) =>
        (await _client.PrimaryNode.GetJsonAsync<InviteScreenModel>(
            $"{ISharedPlanetInvite.BaseRoute}/{code}/screen")).Data;

    /// <summary>
    /// Fetches all planets that the user has joined from the server
    /// </summary>
    public async Task<TaskResult> FetchJoinedPlanetsAsync()
    {
        var response = await _client.PrimaryNode.GetJsonAsync<List<Planet>>($"api/users/me/planets");
        if (!response.Success)
            return response.WithoutData();

        var planets = response.Data;

        _joinedPlanets.Clear();

        planets.SyncAll(_client.Cache);

        // Add to cache
        foreach (var planet in planets)
        {
            _joinedPlanets.Add(planet);
        }

        JoinedPlanetsUpdated?.Invoke();

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
    public Task<TaskResult> TryOpenPlanetConnection(long planetId, string key)
    {
        if (!_client.Cache.Planets.TryGet(planetId, out var planet))
        {
            return Task.FromResult(TaskResult.FromFailure("Planet not found"));
        }

        return TryOpenPlanetConnection(planet, key);
    }

    private void HandlePlanetConnectionFailure(ITaskResult reason, Planet planet, string key)
    {
        LogError(reason.Message);
            
        // Remove lock
        RemovePlanetLock(key);
            
        // Remove from lists
        _connectedPlanets.Remove(planet);
        _connectedPlanetsLookup.Remove(planet.Id);
    }

    /// <summary>
    /// Opens a planet and prepares it for use
    /// </summary>
    public async Task<TaskResult> TryOpenPlanetConnection(Planet planet, string key)
    {
        // Cannot open null
        if (planet is null)
            return TaskResult.FromFailure("Planet is null");

        // Make sure planet is ready (should be, but just in case)
        await planet.EnsureReadyAsync();
        
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
        
        // Joins SignalR group
        var result = await planet.ConnectToRealtime();

        if (!result.Success)
        {
            HandlePlanetConnectionFailure(result, planet, key);
            return result;
        }

        Log(result.Message);
        
        var initialDataResult = await planet.FetchInitialDataAsync();
        if (!initialDataResult.Success)
        {
            HandlePlanetConnectionFailure(initialDataResult, planet, key);
            return initialDataResult;
        }

        sw.Stop();
        
        ConnectedPlanetsUpdated?.Invoke();

        Log($"Time to open this Planet: {sw.ElapsedMilliseconds}ms");

        // Log success
        Log($"Joined SignalR group for planet {planet.Name} ({planet.Id})");

        PlanetConnected?.Invoke(planet);

        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// Closes a SignalR connection to a planet
    /// </summary>
    public Task<TaskResult> TryClosePlanetConnection(long planetId, string key, bool force = false)
    {
        if (!_client.Cache.Planets.TryGet(planetId, out var planet))
        {
            return Task.FromResult(TaskResult.FromFailure("Planet not found"));
        }

        return TryClosePlanetConnection(planet, key, force);
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
        await planet.DisconnectFromRealtime();

        // Remove from list
        _connectedPlanets.Remove(planet);
        _connectedPlanetsLookup.Remove(planet.Id);
        
        ConnectedPlanetsUpdated?.Invoke();

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
            return _planetLocks.Any(x => x.Value == planetId)
                ? ConnectionLockResult.Locked
                : ConnectionLockResult.Unlocked;
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
        JoinedPlanetsUpdated?.Invoke();
    }

    /// <summary>
    /// Removes a planet from the joined planets list and invokes the event.
    /// </summary>
    public void RemoveJoinedPlanet(Planet planet)
    {
        _joinedPlanets.Remove(planet);

        PlanetLeft?.Invoke(planet);
        JoinedPlanetsUpdated?.Invoke();
    }

    public async Task<TaskResult<PlanetMember>> JoinPlanetAsync(long planetId)
    {
        var planet = await FetchPlanetAsync(planetId);
        return await JoinPlanetAsync(planet);
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

    public Task<TaskResult<PlanetMember>> JoinPlanetAsync(long planetId, string inviteCode)
    {
        return _client.PrimaryNode.PostAsyncWithResponse<PlanetMember>(
            $"api/planets/{planetId}/join?inviteCode={inviteCode}");
    }

    /// <summary>
    /// Attempts to leave the given planet
    /// </summary>
    public async Task<TaskResult> LeavePlanetAsync(Planet planet)
    {
        // Get member
        var result = await planet.MyMember.DeleteAsync();

        if (result.Success)
            RemoveJoinedPlanet(planet);

        return result;
    }

    public void SetJoinedPlanets(List<Planet> planets)
    {
        _joinedPlanets.Clear();
        _joinedPlanets.AddRange(planets);
        JoinedPlanetsUpdated?.Invoke();
    }

    public async Task<List<PlanetListInfo>> FetchDiscoverablePlanetsAsync()
    {
        var response = await _client.PrimaryNode.GetJsonAsync<List<PlanetListInfo>>($"api/planets/discoverable");
        if (!response.Success)
            return new List<PlanetListInfo>();
        
        return response.Data;
    }

    public async ValueTask<PlanetRole> FetchRoleAsync(long id, long planetId, bool skipCache = false)
    {
        var planet = await FetchPlanetAsync(planetId, skipCache);
        return await FetchRoleAsync(id, planet, skipCache);
    }

    public async ValueTask<PlanetRole> FetchRoleAsync(long id, Planet planet, bool skipCache = false)
    {
        if (!skipCache && planet.Roles.TryGet(id, out var cached))
            return cached;

        var role = (await planet.Node.GetJsonAsync<PlanetRole>($"{ISharedPlanetRole.GetBaseRoute(planet.Id)}/{id}")).Data;

        return role.Sync(_client);
    }

    public async Task<Dictionary<long, int>> FetchRoleMembershipCountsAsync(long planetId)
    {
        var planet = await FetchPlanetAsync(planetId);
        return await FetchRoleMembershipCountsAsync(planet);
    }

    public async Task<Dictionary<long, int>> FetchRoleMembershipCountsAsync(Planet planet)
    {
        var response = await planet.Node.GetJsonAsync<Dictionary<long, int>>($"{planet.IdRoute}/roles/counts");
        return response.Data;
    }

    public async ValueTask<PlanetMember> FetchMemberByUserAsync(long userId, long planetId, bool skipCache = false)
    {
        var planet = await FetchPlanetAsync(planetId, skipCache);
        return await FetchMemberByUserAsync(userId, planet, skipCache);
    }

    public async ValueTask<PlanetMember> FetchMemberByUserAsync(long userId, Planet planet, bool skipCache = false)
    {
        var key = new PlanetMemberKey(userId, planet.Id);

        if (!skipCache && _client.Cache.MemberKeyToId.TryGetValue(key, out var id) &&
            _client.Cache.PlanetMembers.TryGet(id, out var cached))
            return cached;

        var member =
            (await planet.Node.GetJsonAsync<PlanetMember>(
                $"{ISharedPlanetMember.BaseRoute}/byuser/{planet.Id}/{userId}", true)).Data;

        return _client.Cache.Sync(member);
    }

    public async ValueTask<PlanetMember> FetchMemberAsync(long id, long planetId, bool skipCache = false)
    {
        var planet = await FetchPlanetAsync(planetId, skipCache);
        return await FetchMemberAsync(id, planet, skipCache);
    }

    public async ValueTask<PlanetMember> FetchMemberAsync(long id, Planet planet, bool skipCache = false)
    {
        if (!skipCache && _client.Cache.PlanetMembers.TryGet(id, out var cached))
            return cached;

        var member = (await planet.Node.GetJsonAsync<PlanetMember>($"{ISharedPlanetMember.BaseRoute}/{id}")).Data;

        return _client.Cache.Sync(member);
    }

    public async Task<TaskResult> AddMemberRoleAsync(long memberId, long roleId, long planetId, bool skipCache = false)
    {
        var planet = await FetchPlanetAsync(planetId, skipCache);
        return await planet.Node.PostAsync($"api/planets/{planetId}/members/{memberId}/roles/{roleId}", null);
    }
    
    public async Task<TaskResult> RemoveMemberRoleAsync(long memberId, long roleId, long planetId, bool skipCache = false)
    {
        var planet = await FetchPlanetAsync(planetId, skipCache);
        return await planet.Node.DeleteAsync($"api/planets/{planetId}/members/{memberId}/roles/{roleId}");
    }
    
    private void OnRoleOrderUpdate(RoleOrderEvent e)
    {
        if (!_client.Cache.Planets.TryGet(e.PlanetId, out var planet))
            return;

        planet.NotifyRoleOrderChange(e);
    }

    private async Task OnNodeReconnect(Node node)
    {
        foreach (var planet in _connectedPlanets.Where(x => x.NodeName == node.Name))
        {
            await node.HubConnection.SendAsync("JoinPlanet", planet.Id);
            Log($"Rejoined SignalR group for planet {planet.Id}");
        }
    }

    private void HookHubEvents(Node node)
    {
        node.HubConnection.On<RoleOrderEvent>("RoleOrder-Update", OnRoleOrderUpdate);
    }

}