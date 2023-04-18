﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Valour.Api.Client;

namespace Valour.Api.Nodes
{
    public static class NodeManager
    {
        public static List<Node> Nodes { get; set; } = new List<Node>();
        public static Dictionary<long, Node> PlanetToNode { get; } = new Dictionary<long, Node>();
        public static Dictionary<string, Node> NameToNode { get; } = new Dictionary<string, Node>();
        
        public static Node GetNodeFromName(string name)
        {
            // If the node is not defined, it's ok to
            // use the primary node
            if (string.IsNullOrWhiteSpace(name))
                return ValourClient.PrimaryNode;
            
            NameToNode.TryGetValue(name, out Node node);
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
            PlanetToNode.TryGetValue(planetId, out Node node);

            // Do we already have the node?
            if (node is null)
            {
                // If not, ask current node where the planet is located
                var response = await ValourClient.PrimaryNode.GetAsync($"api/node/planet/{planetId}");

                // We failed to find the planet in a node
                if (!response.Success)
                    return null;

                // If we succeeded, wrap the response in a node object
                var nodeName = response.Data.Trim();

                NameToNode.TryGetValue(nodeName, out node);

                // If we don't already know about this node, create it and link it
                if (node is null) {

                    node = new Node();
                    await node.InitializeAsync(nodeName, ValourClient.Token);
                    Nodes.Add(node);
                    NameToNode[node.Name] = node;
                }
                
                // Set planet to node
                PlanetToNode[planetId] = node;
            }

            return node;
//#else
            // In debug, we only have one node (local)
//            return Nodes[0];
//#endif
        }
    }
}
