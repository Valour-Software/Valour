using System;
using System.Xml.Linq;
using Valour.Server.Database.Items.Planets;
using Valour.Server.Nodes;
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
            Address = config.Address;
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
            app.MapGet("/api/node/handshake", () => new NodeHandshakeResponse()
            {
                Version = Node.Version,
                PlanetIds = Node.Planets.Keys
            });
        }
    }
}

