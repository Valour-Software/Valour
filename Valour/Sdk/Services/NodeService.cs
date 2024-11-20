using Valour.Sdk.Client;
using Valour.Sdk.Nodes;
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
    
    public async Task SetupPrimaryNodeAsync()
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
                nodeName = msg;
            }
        } while (nodeName is null);
        
        // Initialize primary node
        _client.PrimaryNode = new Node(_client);
        await _client.PrimaryNode.InitializeAsync(nodeName, true);
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
                return null;

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