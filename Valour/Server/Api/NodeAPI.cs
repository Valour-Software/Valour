using Valour.Server.Config;
using Valour.Server.Database.Nodes;
using Valour.Server.Hubs;

namespace Valour.Server.API
{
    public class NodeAPI : BaseAPI
	{

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
        public new static void AddRoutes(WebApplication app)
        {
            app.MapGet("api/node/name", () => NodeConfig.Instance.Name);
            
            app.MapGet("api/node/handshake", (NodeLifecycleService service, HostedPlanetService hostedService) => new NodeHandshakeResponse()
            {
                Version = service.Version,
                PlanetIds = hostedService.GetHostedIds()
            });
            
            app.MapGet("api/node/planet/{id}", async (PlanetService planetService, NodeLifecycleService service, long id) =>
            {
                if (!await planetService.ExistsAsync(id))
                    return ValourResult.NotFound("Planet does not exist");
                
                return ValourResult.Ok(await service.GetActiveNodeForPlanetAsync(id));
            });

            app.MapGet("api/nodestats", (ValourDb db) => {
                return db.NodeStats.FirstOrDefaultAsync(x => x.Name == NodeConfig.Instance.Name);
            });

            app.MapGet("api/nodestats/detailed", async (HttpContext ctx, NodeLifecycleService service, HostedPlanetService hostedService, ValourDb db) =>
            {

                var hostedPlanetIds = hostedService.GetHostedIds();
                
                DetailedNodeStats stats = new()
                {
                    Name = NodeConfig.Instance.Name,
                    ConnectionCount = ConnectionTracker.ConnectionIdentities.Count,
                    ConnectionGroupCount = ConnectionTracker.ConnectionGroups.Count,
                    PlanetCount = hostedPlanetIds.Count(),

                    GroupConnections = ConnectionTracker.GroupConnections,
                    GroupUserIds = ConnectionTracker.GroupUserIds,
                    ConnectionGroups = ConnectionTracker.ConnectionGroups,
                    UserIdGroups = ConnectionTracker.UserIdGroups
                };
                
                ctx.Response.Headers.Add("Content-Type", "text/html");

                await ctx.Response.WriteAsync(@"<div style='background-color: black;
                                                                color: white;
                                                                padding: 50px;
                                                                font-family: ubuntu;'>");

                await ctx.Response.WriteAsync($"<h4>Node: {NodeConfig.Instance.Name}</h4> \n" +
                                              $"<hr/><br/>" +
                                              $"<h5>Connections: {ConnectionTracker.ConnectionIdentities.Count}</h5> \n" +
                                              $"<h5>Groups: {ConnectionTracker.ConnectionGroups.Count}</h5> \n" +
                                              $"<h5>HostedPlanets: {hostedPlanetIds.Count()}</h5> \n" +
                                              $"<br/>");

                await ctx.Response.WriteAsync($"<h4>Group Connections:</h4> \n");
                
                foreach (var conn in ConnectionTracker.GroupConnections)
                {
                    await ctx.Response.WriteAsync($"<div style='border: 1px solid salmon; padding: 20px;'>");
                    
                    await ctx.Response.WriteAsync($"<h5>{conn.Key}</h5>");
                    
                    await ctx.Response.WriteAsync($"<div style='border: 1px solid cyan; padding: 20px;'>");
                    
                    foreach (var id in conn.Value)
                    {
                        await ctx.Response.WriteAsync($"<h6>  - {id}</h6>");
                    }
                    
                    await ctx.Response.WriteAsync($"</div>");
                    
                    await ctx.Response.WriteAsync($"</div>");
                }
                
                await ctx.Response.WriteAsync($"<h4>Group User Ids:</h4> \n");

                foreach (var conn in ConnectionTracker.GroupUserIds)
                {
                    await ctx.Response.WriteAsync($"<div style='border: 1px solid salmon; padding: 20px;'>");
                    
                    await ctx.Response.WriteAsync($"<h5>{conn.Key}</h5>");
                    
                    await ctx.Response.WriteAsync($"<div style='border: 1px solid cyan; padding: 20px;'>");
                    
                    foreach (var id in conn.Value)
                    {
                        await ctx.Response.WriteAsync($"<h6>  - {id}</h6>");
                    }
                    
                    await ctx.Response.WriteAsync($"</div>");
                    
                    await ctx.Response.WriteAsync($"</div>");
                }
                
                await ctx.Response.WriteAsync($"<h4>Connection Groups:</h4> \n");
                
                foreach (var conn in ConnectionTracker.ConnectionGroups)
                {
                    await ctx.Response.WriteAsync($"<div style='border: 1px solid salmon; padding: 20px;'>");
                    
                    await ctx.Response.WriteAsync($"<h5>{conn.Key}</h5>");
                    
                    await ctx.Response.WriteAsync($"<div style='border: 1px solid cyan; padding: 20px;'>");
                    
                    foreach (var id in conn.Value)
                    {
                        await ctx.Response.WriteAsync($"<h6>  - {id}</h6>");
                    }
                    
                    await ctx.Response.WriteAsync($"</div>");
                    
                    await ctx.Response.WriteAsync($"</div>");
                }
                
                await ctx.Response.WriteAsync($"<h4>User Id Groups:</h4> \n");
                
                foreach (var conn in ConnectionTracker.UserIdGroups)
                {
                    await ctx.Response.WriteAsync($"<div style='border: 1px solid salmon; padding: 20px;'>");
                    
                    await ctx.Response.WriteAsync($"<h5>{conn.Key}</h5>");
                    
                    await ctx.Response.WriteAsync($"<div style='border: 1px solid cyan; padding: 20px;'>");
                    
                    foreach (var id in conn.Value)
                    {
                        await ctx.Response.WriteAsync($"<h6>  - {id}</h6>");
                    }
                    
                    await ctx.Response.WriteAsync($"</div>");
                    
                    await ctx.Response.WriteAsync($"</div>");
                }
            });
        }
    }
}

