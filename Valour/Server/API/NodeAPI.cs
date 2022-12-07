using System;
using System.Xml.Linq;
using Valour.Api.Nodes;
using Valour.Server.Config;
using Valour.Server.Database;
using Valour.Server.Database.Items.Planets;
using Valour.Server.Database.Nodes;
using Valour.Server.Hubs;
using Valour.Shared.Items.Users;

namespace Valour.Server.API
{
    public class NodeAPI : BaseAPI
	{
        public readonly string Name;
        public readonly string Address;

        public readonly string Version;

        public Dictionary<long, Planet> Planets { get; }

        public static NodeAPI Node;

        public NodeAPI(NodeConfig config)
        {
            Node = this;
            Name = config.Name;
            Address = config.Location;
            Planets = new();
            Version = typeof(ISharedUser).Assembly.GetName().Version.ToString();
        }


        public class NodeHandshakeResponse
        {
            [JsonInclude]
            [JsonPropertyName("version")]
            public string Version { get; set; }

            [JsonInclude]
            [JsonPropertyName("planetIds")]
            public IEnumerable<long> PlanetIds { get; set; }
        }


        /// <summary>
        /// Adds the routes for this API section
        /// </summary>
        public static void AddRoutes(WebApplication app)
        {
            app.MapGet("api/node/handshake", () => new NodeHandshakeResponse()
            {
                Version = Node.Version,
                PlanetIds = Node.Planets.Keys
            });

            app.MapGet("api/nodestats", (ValourDB db) => {
                return db.NodeStats.FirstOrDefaultAsync(x => x.Name == NodeConfig.Instance.Name);
            });

            app.MapGet("api/nodestats/detailed", (ValourDB db) => {

                DetailedNodeStats stats = new()
                {
                    Name = NodeConfig.Instance.Name,
                    ConnectionCount = ConnectionTracker.ConnectionIdentities.Count,
                    ConnectionGroupCount = ConnectionTracker.ConnectionGroups.Count,
                    PlanetCount = Node.Planets.Count,

                    GroupConnections = ConnectionTracker.GroupConnections,
                    GroupUserIds = ConnectionTracker.GroupUserIds,
                    ConnectionGroups = ConnectionTracker.ConnectionGroups,
                    UserIdGroups = ConnectionTracker.UserIdGroups
                };

                return stats;
            });
        }
    }
}

