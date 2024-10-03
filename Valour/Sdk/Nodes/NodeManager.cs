using System.Collections.Concurrent;
using Valour.Sdk.Client;

namespace Valour.Sdk.Nodes
{
    public static class NodeManager
    {
        public static BlockingCollection<Node> Nodes { get; set; } = new(new ConcurrentQueue<Node>());
        public static Dictionary<long, string> PlanetToNode { get; } = new Dictionary<long, string>();
        public static Dictionary<string, Node> NameToNode { get; } = new Dictionary<string, Node>();
        
        public static Node GetNodeFromName(string name)
        {
            // If the node is not defined, it's ok to
            // use the primary node
            if (string.IsNullOrWhiteSpace(name))
                return ValourClient.PrimaryNode;

            // If we don't already know about this node, create it and link it
            if (!NameToNode.TryGetValue(name, out Node node))
            {
                node = new Node();
                NameToNode[name] = node;
                Task.Run(() => node.InitializeAsync(name, ValourClient.Token));
            }
            return node;
        }
        
        /// <summary>
        /// Returns the node that a planet is known to be on,
        /// but will not reach out to the server to find new ones
        /// </summary>
        public static Node GetKnownByPlanet(long planetId)
        {
            PlanetToNode.TryGetValue(planetId, out string name);
            if (name is null)
                return null;

            // If we know the node name but don't have it set up, we can set it up now
            if (!NameToNode.TryGetValue(name, out Node node))
            {
                node = new Node();
                NameToNode[name] = node;
                PlanetToNode[planetId] = name;
                Task.Run(() => node.InitializeAsync(name, ValourClient.Token));
            }

            return node;
        }

        public static void AddNode(Node node)
        {
            NameToNode[node.Name] = node;

            if (!Nodes.Any(x => x.Name == node.Name))
                Nodes.Add(node);
        }

        public static async ValueTask<Node> GetNodeForPlanetAsync(long planetId)
        {

//#if (DEBUG)
            PlanetToNode.TryGetValue(planetId, out string name);

            // Do we already have the node?
            if (name is null)
            {
                // If not, ask current node where the planet is located
                var response = await ValourClient.PrimaryNode.GetAsync($"api/node/planet/{planetId}");

                // We failed to find the planet in a node
                if (!response.Success)
                    return null;

                // If we succeeded, wrap the response in a node object
                name = response.Data.Trim();
            }
            
            NameToNode.TryGetValue(name, out var node);

            // If we don't already know about this node, create it and link it
            if (node is null) {

                node = new Node();
                await node.InitializeAsync(name, ValourClient.Token);
            }
                
            // Set planet to node
            PlanetToNode[planetId] = name;

            return node;
//#else
            // In debug, we only have one node (local)
//            return Nodes[0];
//#endif
        }
    }
}
