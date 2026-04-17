using Valour.Sdk.Client;
using Valour.Sdk.Nodes;
using System.Net.Http.Json;
using Valour.Shared;
using Valour.Shared.Nodes;
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

    public readonly IReadOnlyDictionary<string, Node> OriginToNode;
    private readonly Dictionary<string, Node> _originToNode = new();

    public readonly IReadOnlyList<SavedCommunityNode> SavedCommunityNodes;
    private readonly List<SavedCommunityNode> _savedCommunityNodes = new();

    public readonly IReadOnlyDictionary<string, TaskResult> SavedCommunityNodeStatuses;
    private readonly Dictionary<string, TaskResult> _savedCommunityNodeStatuses = new();
    
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
        OriginToNode = _originToNode;
        SavedCommunityNodes = _savedCommunityNodes;
        SavedCommunityNodeStatuses = _savedCommunityNodeStatuses;
        
        SetupLogging(client.Logger, LogOptions);
    }
    
    public async Task<TaskResult> SetupPrimaryNodeAsync()
    {
        NodeManifest manifest = null;

        do
        {
            manifest = await FetchNodeManifestAsync();
            if (manifest is null)
            {
                LogError("Failed to get primary node manifest... trying again in three seconds.");
                await Task.Delay(3000);
            }
        } while (manifest is null);

        _client.SetAuthorityOrigin(string.IsNullOrWhiteSpace(manifest.AuthorityOrigin)
            ? manifest.CanonicalOrigin
            : manifest.AuthorityOrigin);
        
        // Initialize the home/primary node first.
        _client.HomeNode = new Node(_client);
        var result = await _client.HomeNode.InitializeAsync(manifest, true);
        if (!result.Success)
            return result;

        var authorityResult = await EnsureAuthorityNodeAsync(manifest);
        if (!authorityResult.Success)
        {
            LogWarning($"Failed to initialize authority node: {authorityResult.Message}");
        }

        return result;
    }

    public async Task<TaskResult> EnsureAuthorityNodeAsync(NodeManifest homeManifest = null)
    {
        var authorityOrigin = _client.AuthorityOrigin;
        if (string.IsNullOrWhiteSpace(authorityOrigin))
        {
            authorityOrigin = string.IsNullOrWhiteSpace(homeManifest?.AuthorityOrigin)
                ? homeManifest?.CanonicalOrigin ?? _client.BaseAddress
                : homeManifest.AuthorityOrigin;
            _client.SetAuthorityOrigin(authorityOrigin);
        }

        authorityOrigin = NormalizeOrigin(authorityOrigin);

        if (_client.HomeNode is not null &&
            string.Equals(_client.HomeNode.CanonicalOrigin, authorityOrigin, StringComparison.Ordinal))
        {
            _client.AuthorityNode = _client.HomeNode;
            return TaskResult.SuccessResult;
        }

        if (_client.AuthorityNode is not null &&
            string.Equals(_client.AuthorityNode.CanonicalOrigin, authorityOrigin, StringComparison.Ordinal))
        {
            return TaskResult.SuccessResult;
        }

        var authorityManifest = homeManifest;
        if (authorityManifest is null ||
            !string.Equals(NormalizeOrigin(authorityManifest.CanonicalOrigin), authorityOrigin, StringComparison.Ordinal))
        {
            authorityManifest = await FetchNodeManifestAsync(authorityOrigin);
        }

        if (authorityManifest is null)
        {
            return TaskResult.FromFailure($"Failed to fetch authority node manifest from {authorityOrigin}.");
        }

        var node = new Node(_client);
        var result = await node.InitializeAsync(authorityManifest);
        if (!result.Success)
            return result;

        _client.AuthorityNode = node;
        return TaskResult.SuccessResult;
    }

    public async Task<NodeManifest> FetchNodeManifestAsync(string origin = null)
    {
        HttpClient client = null;
        var createdClient = false;

        try
        {
            if (string.IsNullOrWhiteSpace(origin))
            {
                client = _client.Http;
            }
            else
            {
                createdClient = true;
                client = new HttpClient();
                client.BaseAddress = new Uri(NormalizeOrigin(origin) + "/");
            }

            var response = await client.GetAsync("api/node/manifest");
            if (!response.IsSuccessStatusCode)
                return null;

            var manifest = await response.Content.ReadFromJsonAsync<NodeManifest>();
            if (manifest is null || string.IsNullOrWhiteSpace(manifest.Name))
                return null;

            return manifest;
        }
        catch (Exception ex)
        {
            LogError("Failed to fetch node manifest.", ex);
            return null;
        }
        finally
        {
            if (createdClient)
            {
                client?.Dispose();
            }
        }
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

    public async ValueTask<Node> GetByManifestAsync(NodeManifest manifest)
    {
        if (manifest is null)
            return null;

        if (!string.IsNullOrWhiteSpace(manifest.CanonicalOrigin))
        {
            var normalizedOrigin = NormalizeOrigin(manifest.CanonicalOrigin);
            if (_originToNode.TryGetValue(normalizedOrigin, out var existingByOrigin))
                return existingByOrigin;
        }

        if (_nameToNode.TryGetValue(manifest.Name, out var existing) &&
            string.Equals(existing.NodeId, manifest.NodeId, StringComparison.Ordinal))
        {
            return existing;
        }

        var node = new Node(_client);
        var result = await node.InitializeAsync(manifest);
        if (!result.Success)
        {
            LogError($"Failed to initialize node {manifest.Name}: {result.Message}");
            return null;
        }

        return node;
    }

    public async ValueTask<Node> GetByOriginAsync(string origin)
    {
        if (string.IsNullOrWhiteSpace(origin))
            return null;

        var normalizedOrigin = NormalizeOrigin(origin);
        if (_originToNode.TryGetValue(normalizedOrigin, out var existing))
            return existing;

        var manifest = await FetchNodeManifestAsync(origin);
        return await GetByManifestAsync(manifest);
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
        if (node is null)
            return;

        var normalizedOrigin = string.IsNullOrWhiteSpace(node.CanonicalOrigin)
            ? null
            : NormalizeOrigin(node.CanonicalOrigin);

        if (!string.IsNullOrWhiteSpace(normalizedOrigin))
            _originToNode[normalizedOrigin] = node;

        if (!_nameToNode.TryGetValue(node.Name, out var existingByName) ||
            NodesMatch(existingByName, node) ||
            (existingByName.Mode != NodeMode.Official && node.Mode == NodeMode.Official))
        {
            _nameToNode[node.Name] = node;
        }

        if (_nodes.All(x => !NodesMatch(x, node)))
            _nodes.Add(node);
    }

    public Node GetKnownByOrigin(string origin)
    {
        if (string.IsNullOrWhiteSpace(origin))
            return null;

        _originToNode.TryGetValue(NormalizeOrigin(origin), out var node);
        return node;
    }

    public TaskResult GetSavedCommunityNodeStatus(string origin)
    {
        if (string.IsNullOrWhiteSpace(origin))
            return default;

        _savedCommunityNodeStatuses.TryGetValue(NormalizeOrigin(origin), out var result);
        return result;
    }

    public async Task<TaskResult<List<SavedCommunityNode>>> FetchSavedCommunityNodesAsync()
    {
        if (_client.AccountNode is null)
            return TaskResult<List<SavedCommunityNode>>.FromFailure("No account node is available.");

        var response = await _client.AccountNode.GetJsonAsync<List<SavedCommunityNode>>("api/users/me/communitynodes");
        if (!response.Success)
            return response;

        ReplaceSavedCommunityNodes(response.Data ?? []);
        return response;
    }

    public async Task<TaskResult> LoadSavedCommunityNodesAsync()
    {
        try
        {
            var fetchResult = await FetchSavedCommunityNodesAsync();
            if (!fetchResult.Success)
            {
                LogWarning($"Failed to fetch saved community nodes: {fetchResult.Message}");
                return fetchResult.WithoutData();
            }

            var failures = 0;
            foreach (var savedNode in _savedCommunityNodes.ToList())
            {
                var result = await HandshakeSavedCommunityNodeAsync(savedNode);
                if (!result.Success)
                    failures++;
            }

            if (failures == 0)
                return TaskResult.FromSuccess("Loaded saved community nodes.");

            return new TaskResult(
                true,
                $"Loaded saved community nodes, but {failures} could not be reached.");
        }
        catch (Exception ex)
        {
            LogError("Failed to load saved community nodes.", ex);
            return TaskResult.FromFailure("Failed to load saved community nodes.");
        }
    }

    public async Task<TaskResult<SavedCommunityNode>> AddSavedCommunityNodeAsync(string origin)
    {
        if (_client.AccountNode is null)
            return TaskResult<SavedCommunityNode>.FromFailure("No account node is available.");

        var response = await _client.AccountNode.PostAsyncWithResponse<SavedCommunityNode>(
            "api/users/me/communitynodes",
            new AddCommunityNodeRequest { Origin = origin });

        if (!response.Success || response.Data is null)
            return response;

        UpsertSavedCommunityNode(response.Data);

        var handshakeResult = await HandshakeSavedCommunityNodeAsync(response.Data);
        if (!handshakeResult.Success)
        {
            return new TaskResult<SavedCommunityNode>(
                true,
                $"Node saved, but the client could not connect yet: {handshakeResult.Message}",
                response.Data);
        }

        return new TaskResult<SavedCommunityNode>(
            true,
            $"Saved {response.Data.Name}.",
            response.Data);
    }

    public async Task<TaskResult> RemoveSavedCommunityNodeAsync(long savedNodeId)
    {
        if (_client.AccountNode is null)
            return TaskResult.FromFailure("No account node is available.");

        var result = await _client.AccountNode.DeleteAsync($"api/users/me/communitynodes/{savedNodeId}");
        if (!result.Success)
            return result;

        var existing = _savedCommunityNodes.FirstOrDefault(x => x.Id == savedNodeId);
        if (existing is not null)
        {
            _savedCommunityNodes.Remove(existing);
            if (!string.IsNullOrWhiteSpace(existing.CanonicalOrigin))
                _savedCommunityNodeStatuses.Remove(NormalizeOrigin(existing.CanonicalOrigin));
        }

        return result;
    }

    public async Task<TaskResult> HandshakeSavedCommunityNodeAsync(SavedCommunityNode savedNode)
    {
        if (savedNode is null || string.IsNullOrWhiteSpace(savedNode.CanonicalOrigin))
            return TaskResult.FromFailure("Saved node is missing an origin.");

        var normalizedOrigin = NormalizeOrigin(savedNode.CanonicalOrigin);

        try
        {
            var manifest = await FetchNodeManifestAsync(savedNode.CanonicalOrigin);
            if (manifest is null)
            {
                var failed = TaskResult.FromFailure("Could not reach that node.");
                _savedCommunityNodeStatuses[normalizedOrigin] = failed;
                return failed;
            }

            if (manifest.Mode != NodeMode.Community)
            {
                var failed = TaskResult.FromFailure("That node no longer advertises community mode.");
                _savedCommunityNodeStatuses[normalizedOrigin] = failed;
                return failed;
            }

            var node = await GetByManifestAsync(manifest);
            if (node is null)
            {
                var failed = TaskResult.FromFailure("Failed to initialize that node.");
                _savedCommunityNodeStatuses[normalizedOrigin] = failed;
                return failed;
            }

            UpsertSavedCommunityNode(new SavedCommunityNode
            {
                Id = savedNode.Id,
                NodeId = node.NodeId,
                Name = node.Name,
                CanonicalOrigin = node.CanonicalOrigin,
                AuthorityOrigin = node.AuthorityOrigin,
                Mode = node.Mode,
                TimeAdded = savedNode.TimeAdded
            });

            var success = TaskResult.FromSuccess($"Connected to {node.Name}.");
            _savedCommunityNodeStatuses[NormalizeOrigin(node.CanonicalOrigin)] = success;
            return success;
        }
        catch (Exception ex)
        {
            LogError($"Failed to handshake saved community node {savedNode.CanonicalOrigin}.", ex);
            var failed = TaskResult.FromFailure("Failed to connect to that node.");
            _savedCommunityNodeStatuses[normalizedOrigin] = failed;
            return failed;
        }
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

    private static string NormalizeOrigin(string origin)
    {
        var uri = new Uri(origin, UriKind.Absolute);
        return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
    }

    private void ReplaceSavedCommunityNodes(IEnumerable<SavedCommunityNode> nodes)
    {
        _savedCommunityNodes.Clear();
        _savedCommunityNodeStatuses.Clear();

        foreach (var node in nodes
                     .Where(x => x is not null && !string.IsNullOrWhiteSpace(x.CanonicalOrigin))
                     .OrderBy(x => x.Name)
                     .ThenBy(x => x.CanonicalOrigin))
        {
            UpsertSavedCommunityNode(node);
        }
    }

    private void UpsertSavedCommunityNode(SavedCommunityNode node)
    {
        if (node is null || string.IsNullOrWhiteSpace(node.CanonicalOrigin))
            return;

        node.CanonicalOrigin = NormalizeOrigin(node.CanonicalOrigin);
        if (!string.IsNullOrWhiteSpace(node.AuthorityOrigin))
            node.AuthorityOrigin = NormalizeOrigin(node.AuthorityOrigin);

        var existingIndex = _savedCommunityNodes.FindIndex(x =>
            x.Id == node.Id ||
            string.Equals(x.CanonicalOrigin, node.CanonicalOrigin, StringComparison.Ordinal));

        if (existingIndex >= 0)
            _savedCommunityNodes[existingIndex] = node;
        else
            _savedCommunityNodes.Add(node);

        _savedCommunityNodes.Sort((left, right) =>
        {
            var nameCompare = string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
            return nameCompare != 0
                ? nameCompare
                : string.Compare(left.CanonicalOrigin, right.CanonicalOrigin, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static bool NodesMatch(Node left, Node right)
    {
        if (left is null || right is null)
            return false;

        if (!string.IsNullOrWhiteSpace(left.CanonicalOrigin) &&
            !string.IsNullOrWhiteSpace(right.CanonicalOrigin) &&
            string.Equals(
                NormalizeOrigin(left.CanonicalOrigin),
                NormalizeOrigin(right.CanonicalOrigin),
                StringComparison.Ordinal))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(left.NodeId) &&
               !string.IsNullOrWhiteSpace(right.NodeId) &&
               string.Equals(left.NodeId, right.NodeId, StringComparison.Ordinal) &&
               left.Mode == right.Mode;
    }
}
