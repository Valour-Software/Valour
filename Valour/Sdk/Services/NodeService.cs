using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Valour.Sdk.Client;
using Valour.Sdk.Nodes;
using Valour.Shared;
using Valour.Shared.Models;
using Valour.Shared.Utilities;

namespace Valour.Sdk.Services;

public class NodeService : ServiceBase
{
    public HybridEvent<Node> NodeAdded;
    public HybridEvent<Node> NodeRemoved;
    public HybridEvent<Node> NodeReconnected;

    public readonly IReadOnlyList<Node> Nodes;
    private readonly List<Node> _nodes = new();

    public readonly IReadOnlyDictionary<long, string> PlanetToNodeName;
    private readonly Dictionary<long, string> _planetToNodeName = new();

    public readonly IReadOnlyDictionary<string, Node> NameToNode;
    private readonly Dictionary<string, Node> _nameToNode = new();

    private readonly SemaphoreSlim _federatedSessionLock = new(1, 1);
    private Timer _federationSessionRefreshTimer;
    
    private readonly ValourClient _client;
    
    private static readonly LogOptions LogOptions = new (
        "NodeService",
        "#036bfc",
        "#fc0356",
        "#fc8403"
    );
    
    public NodeService(ValourClient client)
    {
        _client = client;
        
        Nodes = _nodes;
        PlanetToNodeName = _planetToNodeName;
        NameToNode = _nameToNode;
        
        SetupLogging(client.Logger, LogOptions);
    }
    
    public async Task<TaskResult> SetupPrimaryNodeAsync()
    {
        string nodeName = null;

        do
        {
            // Get primary node identity
            var nodeNameResponse = await _client.Http.GetAsync("api/node/name");
            var msg = await nodeNameResponse.Content.ReadAsStringAsync();
            if (!nodeNameResponse.IsSuccessStatusCode)
            {
                LogError("Failed to get primary node name... trying again in three seconds. Network issues? \n \n" + msg);
                await Task.Delay(3000);
            }
            else
            {
                nodeName = msg?.Trim();
                if (string.IsNullOrWhiteSpace(nodeName) || nodeName.Contains('<'))
                {
                    LogError("Received invalid primary node name response... trying again in three seconds. Response was:\n\n" + msg);
                    nodeName = null;
                    await Task.Delay(3000);
                }
            }
        } while (nodeName is null);
        
        // Initialize primary node
        _client.PrimaryNode = new Node(_client);
        
        return await _client.PrimaryNode.InitializeAsync(nodeName, true);
    }
    
    
    /// <summary>
    /// Returns the node that a planet is known to be on,
    /// but will not reach out to the server to find new ones
    /// </summary>
    public Node GetKnownByPlanet(long planetId)
    {
        _planetToNodeName.TryGetValue(planetId, out string name);
        if (name is null)
            return null;

        // If we know the node name but don't have it set up, we can set it up now
        _nameToNode.TryGetValue(name, out Node node);
        return node;
    }

    /// <summary>
    /// Returns the node with the given name
    /// </summary>
    public async ValueTask<Node> GetByName(string name)
    {
        // Do we already have the node?
        if (_nameToNode.TryGetValue(name, out var node))
            return node;
        
        // If not, create it and link it
        node = new Node(_client);
        await node.InitializeAsync(name);
        
        // TODO: We have a master list of node names so we can probably do a sanity check here
        
        return node;
    }

    /// <summary>
    /// Returns the node with the given name, and sets the location of a planet
    /// Used for healing node locations after bad requests
    /// </summary>
    public async Task<Node> GetNodeAndSetPlanetLocation(string name, long? planetId)
    {
        if (name is null)
            return null;
        
        var node = await GetByName(name);

        if (planetId is not null)
        {
            _planetToNodeName[planetId.Value] = name;

            if (_client.Cache.Planets.TryGet(planetId.Value, out var planet))
                planet.SetNode(node);
        }

        return node;
    }

    /// <summary>
    /// Connects to a community (federated) node: gets a hub-minted federation
    /// token for the domain, exchanges it on the node for a node-local session,
    /// and brings up an external Node (HTTP + SignalR to the node's own origin).
    /// Returns the connected node, or null on failure.
    /// </summary>
    public async Task<Node> ConnectToFederatedNodeAsync(string domain, bool insecure = false)
    {
        domain = NormalizeFederationDomain(domain);
        if (domain is null)
            return null;

        await _federatedSessionLock.WaitAsync();
        try
        {
            return await ConnectToFederatedNodeInternalAsync(domain, insecure);
        }
        finally
        {
            _federatedSessionLock.Release();
        }
    }

    /// <summary>
    /// Redeems a recipient-bound federation invite directly with its community
    /// node. When the hub is down this uses the client's cached passport and
    /// the node's cached hub JWKS; it does not make the hub join-time critical.
    /// </summary>
    public async Task<Node> RedeemFederatedInviteAsync(string domain, string grant, bool insecure = false)
    {
        if (string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(grant))
            return null;

        // The passport and proof are sufficient to redeem this exact grant.
        // Verify the grant locally against the hub JWKS cache before using its
        // destination claim; otherwise a modified JWT could redirect this
        // identity material to an attacker-controlled host.
        var destination = await _client.AuthService.GetFederatedInviteDestinationAsync(grant);
        if (!destination.Success)
            return null;

        var expectedDomain = destination.Data;
        var requestedDomain = NormalizeFederationDomain(domain);
        if (expectedDomain is null || requestedDomain is null ||
            !string.Equals(expectedDomain, requestedDomain, StringComparison.Ordinal))
        {
            return null;
        }

        var redemption = await _client.AuthService.CreateFederatedInviteRedeemRequestAsync(grant);
        if (!redemption.Success)
            return null;

        await _federatedSessionLock.WaitAsync();
        try
        {
            _nameToNode.TryGetValue(requestedDomain, out var existing);
            var scheme = insecure ? "http" : "https";
            using var http = new HttpClient();
            var response = await http.PostAsJsonAsync(
                $"{scheme}://{requestedDomain}/api/federation/invites/redeem",
                redemption.Data);
            if (!response.IsSuccessStatusCode)
                return null;

            var authToken = await response.Content.ReadFromJsonAsync<Valour.Sdk.Models.AuthToken>();
            if (string.IsNullOrWhiteSpace(authToken?.Id))
                return null;

            if (existing is not null)
            {
                var refreshed = await existing.RefreshExternalTokenAsync(authToken.Id, authToken.TimeExpires);
                return refreshed.Success ? existing : null;
            }

            var node = new Node(_client);
            var init = await node.InitializeExternalAsync(requestedDomain, authToken.Id, authToken.TimeExpires, insecure);
            if (!init.Success)
                return null;

            EnsureFederationSessionRefreshTimer();
            return node;
        }
        catch
        {
            return null;
        }
        finally
        {
            _federatedSessionLock.Release();
        }
    }

    /// <summary>
    /// Imports a hub-authorized migration on the destination node named by the
    /// signed grant. The SDK deliberately derives the destination from the
    /// grant instead of accepting a separate URL, so a copied grant cannot be
    /// sent to an unrelated host by a misleading UI field.
    /// </summary>
    public async Task<TaskResult> ImportFederatedMigrationAsync(string grant, bool insecure = false)
    {
        var destinationDomain = NormalizeFederationDomain(
            ReadUnvalidatedJwtStringClaim(grant, "aud"));
        if (destinationDomain is null)
            return TaskResult.FromFailure("The migration grant has no valid destination domain.");

        // The hub mints a narrowly scoped federation session for the owner of
        // a pending migration, even before that owner has a regular membership
        // on the destination. The node still validates the signed grant and
        // owner id before it imports anything.
        var node = await ConnectToFederatedNodeAsync(destinationDomain, insecure);
        if (node is null)
            return TaskResult.FromFailure("Could not establish a secure session with the migration destination.");

        return await node.PostAsync(
            "api/federation/migrations/import",
            new MigrationImportRequest { Grant = grant });
    }

    internal static string NormalizeFederationDomain(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
            return null;

        domain = domain.Trim().TrimEnd('/');
        if (domain.Contains("//", StringComparison.Ordinal) || domain.Contains('/') || domain.Contains(' '))
            return null;

        if (!Uri.TryCreate($"https://{domain}", UriKind.Absolute, out var uri) ||
            uri.HostNameType != UriHostNameType.Dns ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            uri.AbsolutePath != "/")
        {
            return null;
        }

        return uri.IsDefaultPort ? uri.Host.ToLowerInvariant() : $"{uri.Host.ToLowerInvariant()}:{uri.Port}";
    }

    private static string ReadUnvalidatedJwtStringClaim(string token, string claim)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3)
                return null;

            var base64 = parts[1].Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
            using var document = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(base64)));
            if (!document.RootElement.TryGetProperty(claim, out var value))
                return null;
            return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
        }
        catch
        {
            return null;
        }
    }

    private async Task<Node> ConnectToFederatedNodeInternalAsync(string domain, bool insecure)
    {
        _nameToNode.TryGetValue(domain, out var existing);

        // 1. Hub mints a federation token for this domain.
        var tokenResult = await _client.PrimaryNode.PostAsyncWithResponse<Valour.Shared.Models.FederationTokenResponse>(
            "api/federation/token", new Valour.Shared.Models.FederationTokenRequest { Domain = domain });
        if (!tokenResult.Success || tokenResult.Data?.Token is null)
            return null;

        // 2. Exchange it on the node's origin for a node-local session token.
        var scheme = insecure ? "http" : "https";
        try
        {
            using var http = new HttpClient();
            var resp = await http.PostAsJsonAsync(
                $"{scheme}://{domain}/api/federation/exchange",
                new Valour.Shared.Models.FederationExchangeRequest { HubToken = tokenResult.Data.Token });
            if (!resp.IsSuccessStatusCode)
                return null;
            var authToken = await resp.Content.ReadFromJsonAsync<Valour.Sdk.Models.AuthToken>();
            if (string.IsNullOrWhiteSpace(authToken?.Id))
                return null;

            if (existing is not null)
            {
                var refreshed = await existing.RefreshExternalTokenAsync(authToken.Id, authToken.TimeExpires);
                return refreshed.Success ? existing : null;
            }

            // 3. Bring up the external node (HTTP + SignalR to the node origin).
            var node = new Node(_client);
            var init = await node.InitializeExternalAsync(domain, authToken.Id, authToken.TimeExpires, insecure);
            if (!init.Success)
                return null;

            EnsureFederationSessionRefreshTimer();
            return node;
        }
        catch
        {
            return null;
        }
    }

    private void EnsureFederationSessionRefreshTimer()
    {
        _federationSessionRefreshTimer ??= new Timer(
            _ => _ = RefreshExpiringFederatedSessionsAsync(),
            null,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(5));
    }

    private async Task RefreshExpiringFederatedSessionsAsync()
    {
        try
        {
            var expiringNodes = _nameToNode.Values
                .Where(node => node.IsExternal && node.NeedsFederationSessionRefresh)
                .ToList();

            foreach (var node in expiringNodes)
                await ConnectToFederatedNodeAsync(node.Name, node.ExternalInsecure);
        }
        catch (Exception ex)
        {
            LogWarning("Failed to refresh a federated node session: " + ex.Message);
        }
    }

    public void RegisterNode(Node node)
    {
        _nameToNode[node.Name] = node;

        if (_nodes.All(x => x.Name != node.Name))
            _nodes.Add(node);
    }

    /// <summary>
    /// An external node bearer is bound to the hub account that obtained it.
    /// Never retain it across login, logout, or token replacement; otherwise a
    /// reused SDK client could let the next account operate on the previous
    /// account's community membership.
    /// </summary>
    internal void InvalidateFederatedSessions(string reason)
    {
        var externalNodes = _nodes.Where(node => node.IsExternal).ToList();
        if (externalNodes.Count == 0)
            return;

        var externalNames = externalNodes
            .Select(node => node.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var node in externalNodes)
            node.InvalidateExternalSession(reason);

        foreach (var name in externalNames)
            _nameToNode.Remove(name);

        _nodes.RemoveAll(node => node.IsExternal);

        foreach (var planetId in _planetToNodeName
                     .Where(pair => externalNames.Contains(pair.Value))
                     .Select(pair => pair.Key)
                     .ToList())
        {
            _planetToNodeName.Remove(planetId);
        }
    }
    
    public void SetKnownByPlanet(long planetId, string nodeName)
    {
        // A planet payload fetched directly from a node does not include the
        // hub-internal node name. Never let that absent field erase an
        // established routing decision (especially an external domain).
        if (string.IsNullOrWhiteSpace(nodeName))
            return;

        _planetToNodeName[planetId] = nodeName;
    }

    public async ValueTask<Node> GetNodeForPlanetAsync(long planetId)
    {
            
        _planetToNodeName.TryGetValue(planetId, out string name);

        // Do we already have the node?
        if (name is null)
        {
            // If not, ask current node where the planet is located
            var response = await _client.PrimaryNode.GetAsync($"api/node/planet/{planetId}");

            // We failed to find the planet in a node
            if (!response.Success)
            {
                LogError($"Failed to find node for planet {planetId}: {response.Message}");
                return null;
            }

            // If we succeeded, wrap the response in a node object
            name = response.Data.Trim();
        }
            
        NameToNode.TryGetValue(name, out var node);

        // If we don't already know about this node, create it and link it
        if (node is null) {

            node = new Node(_client);
            await node.InitializeAsync(name);
        }
                
        // Set planet to node
        _planetToNodeName[planetId] = name;

        return node;
    }

    public void CheckConnections()
    {
        foreach (var node in Nodes)
        {
            node.CheckConnection();
        }
    }
}
