using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Valour.Sdk.Client;
using Valour.Sdk.ModelLogic;
using Valour.Sdk.Nodes;
using Valour.Shared;
using Valour.Shared.Models;
using Valour.Shared.Utilities;
using Valour.Sdk.Models;

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

    /// <summary>
    /// Tracks planet IDs currently being connected to prevent concurrent setup
    /// </summary>
    private readonly HashSet<long> _connectingPlanets = new();

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
        {
            if (cached.MyMember is null)
            {
                await cached.EnsureReadyAsync();
            }

            return cached;
        }

        // A community planet is not present in the hub database. Prefer an
        // already-known mapping (normal for joined planets), then ask the
        // official node router for regular planets. If neither knows the id,
        // resolve the federation stub and establish the external connection.
        // This also makes direct links work before the home-page membership
        // bootstrap has had a chance to populate the mapping.
        var node = _client.NodeService.GetKnownByPlanet(id)
                   ?? await _client.NodeService.GetNodeForPlanetAsync(id);

        if (node is null)
        {
            var location = await FetchFederatedLocationAsync(id);
            if (location is not null)
            {
                node = await _client.NodeService.ConnectToFederatedNodeAsync(location.NodeDomain);
                if (node is not null)
                    _client.NodeService.SetKnownByPlanet(id, node.Name);
            }
        }

        node ??= _client.PrimaryNode;
        var planetResult = await node.GetJsonAsync<Planet>($"api/planets/{id}");
        if (!planetResult.Success || planetResult.Data is null)
        {
            LogError($"Failed to fetch planet {id}: {planetResult.Message}");
            return null;
        }

        var planet = planetResult.Data;

        if (node.IsExternal)
            planet.NodeName = node.Name;

        planet = planet.Sync(_client);
        if (node.IsExternal)
            planet.SetNode(node);
        
        await planet.EnsureReadyAsync();

        return planet;
    }

    /// <summary>
    /// Fetches initial data for planet setup
    /// </summary>
    public async Task<TaskResult> FetchInitialPlanetDataAsync(long planetId)
    {
        var planet = await FetchPlanetAsync(planetId);
        if (planet is null)
            return TaskResult.FromFailure($"Planet {planetId} could not be loaded.");

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
            data.Emojis?.SyncAll(_client);
            data.Rules?.SyncAll(_client);
            _client.VoiceStateService.SetInitialVoiceState(data.VoiceParticipants);
        }
        
        return result.WithoutData();
    }

    /// <summary>
    /// Returns the invite for the given invite code (id)
    /// </summary>
    public async Task<PlanetInvite> FetchInviteAsync(string code, bool skipCache = false)
    {
        if (_client.Cache.OutsidePlanetInvites.TryGet(code, out var cached))
            return cached;

        var invite = (await _client.PrimaryNode.GetJsonAsync<PlanetInvite>(ISharedPlanetInvite.GetIdRoute(code))).Data;

        return invite.Sync(_client);
    }

    public Task<TaskResult<PlanetListInfo>> FetchInviteScreenDataAsync(string code) =>
        _client.PrimaryNode.GetJsonAsync<PlanetListInfo>($"{ISharedPlanetInvite.BaseRoute}/{code}/screen");

    /// <summary>
    /// Fetches public planet information by ID (no membership required)
    /// </summary>
    public async Task<TaskResult<PlanetListInfo>> FetchPlanetInfoAsync(long planetId)
    {
        var response = await _client.PrimaryNode.GetJsonAsync<PlanetListInfo>($"api/planets/{planetId}/info");
        if (!response.Success)
            return TaskResult<PlanetListInfo>.FromFailure(response.Message);
        
        var planetInfo = response.Data;
        planetInfo.Sync(_client);
        return TaskResult<PlanetListInfo>.FromData(planetInfo);
    }

    /// <summary>
    /// Fetches all planets that the user has joined from the server
    /// </summary>
    public async Task<TaskResult> FetchJoinedPlanetsAsync()
    {
        var response = await _client.PrimaryNode.GetJsonAsync<List<Planet>>($"api/users/me/planets");
        if (!response.Success)
            return response.WithoutData();

        var memberships = await FetchFederatedMembershipsAsync();
        await ApplyJoinedPlanetsAsync(response.Data, memberships);
        return TaskResult.SuccessResult;
    }

    public async Task ApplyJoinedPlanetsAsync(
        IEnumerable<Planet> joinedPlanets,
        IEnumerable<FederatedMembershipInfo> federatedMemberships)
    {
        var planets = (joinedPlanets ?? []).ToList();

        _joinedPlanets.Clear();

        planets.SyncAll(_client);

        // Add to cache
        foreach (var planet in planets)
        {
            _joinedPlanets.Add(planet);
        }

        // Also render community-hosted memberships: connect to each node, map the
        // planet to it, and load it from its own origin. Best-effort per node so a
        // single offline community server doesn't break the whole list.
        foreach (var membership in federatedMemberships ?? [])
        {
            try
            {
                var node = await _client.NodeService.ConnectToFederatedNodeAsync(membership.NodeDomain);
                if (node is null)
                    continue;

                _client.NodeService.SetKnownByPlanet(membership.PlanetId, node.Name);

                var planetResult = await node.GetJsonAsync<Planet>($"api/planets/{membership.PlanetId}");
                if (planetResult.Success && planetResult.Data is not null)
                {
                    planetResult.Data.NodeName = node.Name;
                    var planet = planetResult.Data.Sync(_client);
                    planet.SetNode(node);
                    if (_joinedPlanets.All(x => x.Id != planet.Id))
                        _joinedPlanets.Add(planet);
                }
            }
            catch (Exception ex)
            {
                LogError($"Failed to load community-hosted planet {membership.PlanetId} on {membership.NodeDomain}: {ex.Message}");
            }
        }

        JoinedPlanetsUpdated?.Invoke();
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
        try
        {
            await planet.EnsureReadyAsync();
        }
        catch (Exception ex)
        {
            LogError($"Failed to prepare planet {planet.Id} for opening.", ex);
            return TaskResult.FromFailure("Failed to prepare planet connection.");
        }
        
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
        if (_connectedPlanetsLookup.ContainsKey(planet.Id))
            return TaskResult.SuccessResult;

        // Already being connected by another concurrent call
        if (_connectingPlanets.Contains(planet.Id))
            return TaskResult.SuccessResult;

        _connectingPlanets.Add(planet.Id);

        Log($"Opening planet {planet.Name} ({planet.Id})");

        var sw = new Stopwatch();

        sw.Start();

        try
        {
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

            // Mark as opened only after successful setup
            _connectedPlanets.Add(planet);
            _connectedPlanetsLookup[planet.Id] = planet;

            sw.Stop();

            ConnectedPlanetsUpdated?.Invoke();

            Log($"Time to open this Planet: {sw.ElapsedMilliseconds}ms");

            // Log success
            Log($"Joined SignalR group for planet {planet.Name} ({planet.Id})");

            PlanetConnected?.Invoke(planet);

            return TaskResult.SuccessResult;
        }
        catch (Exception ex)
        {
            LogError($"Unexpected exception opening planet {planet.Id}: {ex.Message}");
            HandlePlanetConnectionFailure(TaskResult.FromFailure("Unexpected exception opening planet connection."), planet, key);
            return TaskResult.FromFailure("Unexpected exception opening planet connection.");
        }
        finally
        {
            _connectingPlanets.Remove(planet.Id);
        }
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

    // ================= Federation (community-hosted planets) =================

    /// <summary>
    /// Resolves where a community-hosted planet lives and whether the user has
    /// accepted its node's domain. Null when it's an official planet.
    /// </summary>
    public async Task<FederatedPlanetLocation> FetchFederatedLocationAsync(long planetId)
    {
        var result = await _client.PrimaryNode.GetJsonAsync<FederatedPlanetLocation>(
            $"api/federation/planets/{planetId}/location", allow404: true);
        return result.Success ? result.Data : null;
    }

    /// <summary>Adds a community node's domain to the user's accepted list.</summary>
    public Task<TaskResult> AcceptFederationDomainAsync(string domain) =>
        _client.PrimaryNode.PostAsync("api/federation/accepted-domains", new AcceptDomainRequest { Domain = domain });

    /// <summary>Returns the community domains the user has explicitly accepted.</summary>
    public async Task<List<string>> FetchAcceptedFederationDomainsAsync()
    {
        var result = await _client.PrimaryNode.GetJsonAsync<List<string>>("api/federation/accepted-domains");
        return result.Success ? result.Data : new List<string>();
    }

    /// <summary>Records a join of a community-hosted planet (domain must be accepted first).</summary>
    public Task<TaskResult<FederatedPlanetLocation>> JoinFederatedPlanetAsync(long planetId) =>
        _client.PrimaryNode.PostAsyncWithResponse<FederatedPlanetLocation>(
            $"api/federation/planets/{planetId}/join", null);

    /// <summary>
    /// Revokes the hub-side federation membership for a community-hosted planet.
    /// Without this, leaving only removes the node-local member while the hub
    /// grant persists — the user could silently re-materialize membership.
    /// </summary>
    public Task<TaskResult> LeaveFederatedPlanetAsync(long planetId) =>
        _client.PrimaryNode.PostAsync($"api/federation/planets/{planetId}/leave", null);

    /// <summary>The user's community-hosted memberships ("planets on other servers").</summary>
    public async Task<List<FederatedMembershipInfo>> FetchFederatedMembershipsAsync()
    {
        var result = await _client.PrimaryNode.GetJsonAsync<List<FederatedMembershipInfo>>("api/federation/memberships");
        return result.Success ? result.Data : new List<FederatedMembershipInfo>();
    }

    public async Task<TaskResult<PlanetMember>> JoinPlanetAsync(long planetId)
    {
        // Route the join to the node that hosts the planet so its in-memory member cache stays
        // authoritative. Fall back to the primary node only if the host can't be resolved.
        var node = await _client.NodeService.GetNodeForPlanetAsync(planetId) ?? _client.PrimaryNode;
        var result = await node.PostAsyncWithResponse<PlanetMember>($"api/planets/{planetId}/discover");

        if (result.Success)
        {
            // Get the planet
            var planet = await FetchPlanetAsync(planetId);
            if (planet == null)
            {
                // If we can't find the planet, return failure
                return TaskResult<PlanetMember>.FromFailure("Planet not found after joining. Try a refresh.");
            }

            planet.Sync(_client);
            AddJoinedPlanet(planet);
        }

        return result;
    }

    public async Task<TaskResult<PlanetMember>> JoinPlanetAsync(long planetId, string inviteCode)
    {
        // Route the join to the planet's hosting node (see JoinPlanetAsync above).
        var node = await _client.NodeService.GetNodeForPlanetAsync(planetId) ?? _client.PrimaryNode;
        var result = await node.PostAsyncWithResponse<PlanetMember>(
            $"api/planets/{planetId}/join?inviteCode={inviteCode}");

        if (result.Success)
        {
            var planet = await FetchPlanetAsync(planetId);
            if (planet != null)
            {
                planet.Sync(_client);
                AddJoinedPlanet(planet);
            }
        }

        return result;
    }

    /// <summary>
    /// Attempts to leave the given planet
    /// </summary>
    public async Task<TaskResult> LeavePlanetAsync(Planet planet)
    {
        // Get member (don't use Planet.MyMember because the planet may not be opened)
        var myMember = await planet.FetchMemberByUserAsync(_client.Me.Id);
        if (myMember == null)
        {
            LogError($"Failed to leave planet {planet.Name} ({planet.Id}): Not a member.");
            return TaskResult.FromFailure("Membership not found.");
        }
        
        var isFederated = _client.NodeService.GetKnownByPlanet(planet.Id)?.IsExternal == true;
        if (isFederated)
        {
            // The hub is the durable membership authority. Revoke it before
            // deleting the node-local row: otherwise a transient hub failure
            // can make a user appear to leave while the next token exchange
            // silently materializes the membership again.
            var fedLeave = await LeaveFederatedPlanetAsync(planet.Id);
            if (!fedLeave.Success)
                return TaskResult.FromFailure(
                    $"Could not revoke the federation membership at the hub. Your community membership was not changed. {fedLeave.Message}");
        }

        var result = await myMember.DeleteAsync();
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
        
        var planets = response.Data;
        planets.SyncAll(_client);
        return planets;
    }

    public ModelQueryEngine<PlanetListInfo> CreateDiscoverablePlanetsQueryEngine()
    {
        return new ModelQueryEngine<PlanetListInfo>(_client.PrimaryNode, "api/planets/discoverable/query");
    }

    public async ValueTask<PlanetRole> FetchRoleAsync(long id, long planetId, bool skipCache = false)
    {
        var planet = await FetchPlanetAsync(planetId, skipCache);
        if (planet is null)
            return null;

        return await FetchRoleAsync(id, planet, skipCache);
    }

    public async ValueTask<PlanetRole> FetchRoleAsync(long id, Planet planet, bool skipCache = false)
    {
        if (!skipCache && planet.Roles.TryGet(id, out var cached))
            return cached;

        var role = (await planet.Node.GetJsonAsync<PlanetRole>($"{ISharedPlanetRole.GetBaseRoute(planet.Id)}/{id}")).Data;

        return role.Sync(_client);
    }

    public async ValueTask<PlanetEmoji> FetchEmojiAsync(long id, long planetId, bool skipCache = false)
    {
        var planet = await FetchPlanetAsync(planetId, skipCache);
        if (planet is null)
            return null;

        return await FetchEmojiAsync(id, planet, skipCache);
    }

    public async ValueTask<PlanetEmoji> FetchEmojiAsync(long id, Planet planet, bool skipCache = false)
    {
        if (!skipCache && planet.Emojis.TryGet(id, out var cached))
            return cached;

        var emoji = (await planet.Node.GetJsonAsync<PlanetEmoji>($"{ISharedPlanetEmoji.GetBaseRoute(planet.Id)}/{id}")).Data;

        return emoji?.Sync(_client);
    }

    public async ValueTask<PlanetRule> FetchRuleAsync(long id, long planetId, bool skipCache = false)
    {
        var planet = await FetchPlanetAsync(planetId, skipCache);
        if (planet is null)
            return null;

        return await FetchRuleAsync(id, planet, skipCache);
    }

    public async ValueTask<PlanetRule> FetchRuleAsync(long id, Planet planet, bool skipCache = false)
    {
        if (!skipCache && planet.Rules.TryGet(id, out var cached))
            return cached;

        var rule = (await planet.Node.GetJsonAsync<PlanetRule>(ISharedPlanetRule.GetIdRoute(planet.Id, id))).Data;
        return rule?.Sync(_client);
    }

    public async Task<int?> FetchMemberCountAsync(Planet planet)
    {
        var response = await planet.Node.GetJsonAsync<int>($"{planet.IdRoute}/members/count");
        if (response.Success)
            return response.Data;

        var info = await FetchPlanetInfoAsync(planet.Id);
        if (info.Success && info.Data is not null)
            return info.Data.MemberCount;

        return null;
    }

    private static readonly TimeSpan PresenceCacheTime = TimeSpan.FromSeconds(60);
    private readonly Dictionary<long, (DateTime Time, Task<PlanetPresenceSummary> Task)> _presenceCache = new();

    /// <summary>
    /// Fetches a snapshot of recently active members on the planet.
    /// Results are briefly cached and deduplicated, so this is safe to call
    /// from many cards in a feed at once.
    /// </summary>
    public Task<PlanetPresenceSummary> FetchPresenceAsync(Planet planet)
    {
        lock (_presenceCache)
        {
            if (_presenceCache.TryGetValue(planet.Id, out var cached) &&
                DateTime.UtcNow - cached.Time < PresenceCacheTime)
                return cached.Task;

            var task = FetchPresenceInternalAsync(planet);
            _presenceCache[planet.Id] = (DateTime.UtcNow, task);
            return task;
        }
    }

    private async Task<PlanetPresenceSummary> FetchPresenceInternalAsync(Planet planet)
    {
        var response = await planet.Node.GetJsonAsync<PlanetPresenceSummary>($"{planet.IdRoute}/presence");
        return response.Success ? response.Data : null;
    }

    public async Task<Dictionary<long, int>> FetchRoleMembershipCountsAsync(long planetId)
    {
        var planet = await FetchPlanetAsync(planetId);
        if (planet is null)
            return new Dictionary<long, int>();

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
        if (planet is null)
            return null;

        return await FetchMemberByUserAsync(userId, planet, skipCache);
    }

    public async ValueTask<PlanetMember> FetchMemberByUserAsync(long userId, Planet planet, bool skipCache = false)
    {
        var key = new PlanetMemberKey(userId, planet.Id);

        if (!skipCache && _client.Cache.MemberKeyToId.TryGetValue(key, out var id) &&
            planet.Members.TryGet(id, out var cached))
            return cached;

        var response = await planet.Node.GetJsonAsync<PlanetMember>(
            $"{ISharedPlanetMember.BaseRoute}/byuser/{planet.Id}/{userId}", true);
        if (!response.Success)
        {
            LogError($"Failed to fetch member by user ({userId}) in planet {planet.Id}: {response.Message}");
            return null;
        }

        if (response.Data is null)
        {
            LogWarning($"Planet member lookup returned null for user {userId} in planet {planet.Id}.");
            return null;
        }

        return response.Data.Sync(_client);
    }

    public async ValueTask<PlanetMember> FetchMemberAsync(long id, long planetId, bool skipCache = false)
    {
        var planet = await FetchPlanetAsync(planetId, skipCache);
        if (planet is null)
            return null;

        return await FetchMemberAsync(id, planet, skipCache);
    }

    public async ValueTask<PlanetMember> FetchMemberAsync(long id, Planet planet, bool skipCache = false)
    {
        if (!skipCache && planet.Members.TryGet(id, out var cached))
            return cached;

        var member = (await planet.Node.GetJsonAsync<PlanetMember>($"{ISharedPlanetMember.BaseRoute}/{id}", true)).Data;

        return member?.Sync(_client);
    }

    public async Task<TaskResult> AddMemberRoleAsync(long memberId, long roleId, long planetId, bool skipCache = false)
    {
        var planet = await FetchPlanetAsync(planetId, skipCache);
        if (planet is null)
            return TaskResult.FromFailure($"Planet {planetId} could not be loaded.");

        return await planet.Node.PostAsync($"api/planets/{planetId}/members/{memberId}/roles/{roleId}", null);
    }
    
    public async Task<TaskResult> RemoveMemberRoleAsync(long memberId, long roleId, long planetId, bool skipCache = false)
    {
        var planet = await FetchPlanetAsync(planetId, skipCache);
        if (planet is null)
            return TaskResult.FromFailure($"Planet {planetId} could not be loaded.");

        return await planet.Node.DeleteAsync($"api/planets/{planetId}/members/{memberId}/roles/{roleId}");
    }

    public ModelQueryEngine<PlanetMember> GetMemberQueryEngine(Planet planet) =>
        new ModelQueryEngine<PlanetMember>(planet.Node, $"api/planets/{planet.Id}/members");

    public ModelQueryEngine<PlanetBan> GetBanQueryEngine(Planet planet) =>
        new ModelQueryEngine<PlanetBan>(planet.Node, $"api/planets/{planet.Id}/bans");

    public ModelQueryEngine<PlanetReport> GetReportQueryEngine(Planet planet) =>
        new ModelQueryEngine<PlanetReport>(planet.Node, $"{ISharedPlanetReport.GetBaseRoute(planet.Id)}/query");
    
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
        node.HubConnection.On<RoleOrderEvent>("RoleOrder-Update", update =>
        {
            if (node.AcceptsExternalPlanetRealtimeEvent(update?.PlanetId))
                OnRoleOrderUpdate(update);
        });
    }

    ////////////
    // Vanity //
    ////////////

    public async Task<TaskResult<long>> ResolveVanityAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return TaskResult<long>.FromFailure("Name is required.");

        return await _client.PrimaryNode.GetJsonAsync<long>(
            ISharedPlanet.GetVanityResolveRoute(name.Trim().ToLowerInvariant()));
    }

    public async Task<TaskResult> CheckVanityAvailableAsync(Planet planet, string name)
    {
        var route = $"{ISharedPlanet.GetVanityCheckRoute(planet.Id)}?name={Uri.EscapeDataString(name ?? string.Empty)}";
        var response = await planet.Node.GetJsonAsync<TaskResult>(route);

        return response.Success ? response.Data : TaskResult.FromFailure(response.Message);
    }

    public async Task<TaskResult> SetVanityAsync(Planet planet, string name)
    {
        var response = await planet.Node.PutAsyncWithResponse<TaskResult>(
            ISharedPlanet.GetVanityRoute(planet.Id),
            new PlanetVanityRequest { Name = name });

        return response.Success ? response.Data : TaskResult.FromFailure(response.Message);
    }
}
