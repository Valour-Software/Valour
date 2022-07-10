using System;
using Valour.Server.Nodes;
using Valour.Shared.Items.Users;

namespace Valour.Server.API
{
	public class NodeAPI : BaseAPI
	{
        public class NodeHandshakeResponse
        {
            public string Version { get; set; }
            public IEnumerable<long> PlanetIds { get; set; }
        }


        /// <summary>
        /// Adds the routes for this API section
        /// </summary>
        public static void AddRoutes(WebApplication app)
        {
            app.MapGet("/api/node/handshake", () => new NodeHandshakeResponse()
            {
                Version = typeof(ISharedUser).Assembly.GetName().Version.ToString(),
                PlanetIds = DeployedNode.Instance.Planets.Keys
            });
        }
    }
}

