using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Valour.Api.Client;

namespace Valour.Api.Nodes
{
    public static class NodeManager
    {
        public static Dictionary<ulong, string> PlanetToNode { get; } = new Dictionary<ulong, string>();
        public static Dictionary<ulong, string> PlanetToNodeLocation { get; } = new Dictionary<ulong, string>();

        const string CoreLocation = "https://core.valour.gg";


        public static async Task<string> GetLocation(ulong planetId)
        {
            if (PlanetToNodeLocation.ContainsKey(planetId))
                return PlanetToNodeLocation[planetId];

            string node = await GetPlanetNode(planetId);
            string location = null;

            if (node is not null) {
                location = $"https://{node}.nodes.valour.gg";
                PlanetToNodeLocation[planetId] = location;
            }

            return location;
        }
            

#if DEBUG
        public static async Task<string> GetNode(ulong planetId) => "";
#else
        public static async Task<string> GetNode(ulong planetId)
        {
            if (PlanetToNode.ContainsKey(planetId))
                return PlanetToNode[planetId];

            string location = await ValourClient.GetAsync(CoreLocation + $"/locate/{planetId}");

            if (location is not null)
                PlanetToNode.Add(planetId, location);

            return location;
        }
#endif
    }
}
