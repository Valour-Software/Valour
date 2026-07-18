using System.Net.Http.Json;
using Valour.Sdk.Client;
using Valour.Sdk.Nodes;
using Valour.Shared;
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
        if (string.IsNullOrWhiteSpace(domain))
            return null;

        if (_nameToNode.TryGetValue(domain, out var existing))
            return existing;

        // 1. Hub mints a federation token for this domain.
        var tokenResult = await _client.PrimaryNode.PostAsyncWithResponse<Valour.Shared.Models.FederationTokenResponse>(
            "api/federation/token", new Valour.Shared.Models.FederationTokenRequest { Domain = domain });
        if (!tokenResult.Success || tokenResult.Data?.Token is null)
            return null;

        // 2. Exchange it on the node's origin for a node-local session token.
        var scheme = insecure ? "http" : "https";
        string nodeLocalToken;
        try
        {
            using var http = new HttpClient();
            var resp = await http.PostAsJsonAsync(
                $"{scheme}://{domain}/api/federation/exchange",
                new Valour.Shared.Models.FederationExchangeRequest { HubToken = tokenResult.Data.Token });
            if (!resp.IsSuccessStatusCode)
                return null;
            var authToken = await resp.Content.ReadFromJsonAsync<Valour.Sdk.Models.AuthToken>();
            nodeLocalToken = authToken?.Id;
        }
        catch
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(nodeLocalToken))
            return null;

        // 3. Bring up the external node (HTTP + realtime to the node origin).
        var node = new Node(_client);
        var init = await node.InitializeExternalAsync(domain, nodeLocalToken, insecure);
        return init.Success ? node : null;
    }

    public void RegisterNode(Node node)
    {
        _nameToNode[node.Name] = node;

        if (_nodes.All(x => x.Name != node.Name))
            _nodes.Add(node);
    }
    
    public void SetKnownByPlanet(long planetId, string nodeName)
    {
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
