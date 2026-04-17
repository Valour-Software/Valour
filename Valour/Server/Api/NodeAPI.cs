using Microsoft.AspNetCore.Mvc;
using Valour.Config.Configs;
using Valour.Server.Database.Nodes;
using Valour.Server.Hubs;
using Valour.Shared.Authorization;
using Valour.Shared.Nodes;

namespace Valour.Server.API
{
    public class NodeAPI : BaseAPI
	{
        /// <summary>
        /// Adds the routes for this API section
        /// </summary>
        public new static void AddRoutes(WebApplication app)
        {
            app.MapGet("api/node/name", () => NodeConfig.Instance.Name);

            app.MapGet("api/node/manifest", (
                HttpContext ctx,
                NodeLifecycleService service,
                HostedPlanetService hostedService) => BuildManifest(ctx, service, hostedService));
            
            app.MapGet("api/node/handshake", (
                HttpContext ctx,
                NodeLifecycleService service,
                HostedPlanetService hostedService) => BuildManifest(ctx, service, hostedService));

            app.MapPost("api/node/community-token", async (
                [FromBody] CommunityNodeTokenExchangeRequest request,
                TokenService tokenService,
                UserService userService,
                CommunityNodeTokenService communityNodeTokenService) =>
            {
                if (NodeConfig.Instance.Mode != NodeMode.Official)
                    return ValourResult.Forbid("Only official nodes can mint community tokens.");

                if (request is null ||
                    string.IsNullOrWhiteSpace(request.NodeId) ||
                    string.IsNullOrWhiteSpace(request.CanonicalOrigin))
                {
                    return ValourResult.BadRequest("NodeId and CanonicalOrigin are required.");
                }

                var currentToken = await tokenService.GetCurrentTokenAsync();
                if (currentToken is null)
                    return ValourResult.InvalidToken();

                if (!currentToken.HasScope(UserPermissions.FullControl))
                    return ValourResult.LacksPermission(UserPermissions.FullControl);

                var user = await userService.GetCurrentUserAsync();
                if (user is null)
                    return ValourResult.NotFound<User>();

                var exchange = await communityNodeTokenService.IssueAsync(
                    user,
                    request.NodeId,
                    request.CanonicalOrigin);

                return Results.Json(exchange);
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

            app.MapGet("api/nodestats/detailed", async (HttpContext ctx, SignalRConnectionService connectionService, HostedPlanetService hostedService, ValourDb db) =>
            {
                return "Working on it";
                
                /*
                var hostedPlanetIds = hostedService.GetHostedIds();
                
                DetailedNodeStats stats = new()
                {
                    Name = NodeConfig.Instance.Name,
                    
                    ConnectionCount = SignalRConnectionService.ConnectionIdentities.Count,
                    ConnectionGroupCount = SignalRConnectionService.ConnectionGroups.Count,
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
                                              $"<h5>Connections: {SignalRConnectionService.ConnectionIdentities.Count}</h5> \n" +
                                              $"<h5>Groups: {SignalRConnectionService.ConnectionGroups.Count}</h5> \n" +
                                              $"<h5>HostedPlanets: {hostedPlanetIds.Count()}</h5> \n" +
                                              $"<br/>");

                await ctx.Response.WriteAsync($"<h4>Group Connections:</h4> \n");
                
                foreach (var conn in SignalRConnectionService.GroupConnections)
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

                foreach (var conn in SignalRConnectionService.GroupUserIds)
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
                
                foreach (var conn in SignalRConnectionService.ConnectionGroups)
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
                
                foreach (var conn in SignalRConnectionService.UserIdGroups)
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
                
                */
            });
        }

        private static NodeManifest BuildManifest(
            HttpContext ctx,
            NodeLifecycleService service,
            HostedPlanetService hostedService)
        {
            var configuredOrigin = NodeConfig.Instance.CanonicalOrigin;
            var origin = !string.IsNullOrWhiteSpace(configuredOrigin)
                ? CommunityNodeTokenService.NormalizeOrigin(configuredOrigin)
                : CommunityNodeTokenService.NormalizeOrigin($"{ctx.Request.Scheme}://{ctx.Request.Host}");
            var authorityOrigin = !string.IsNullOrWhiteSpace(NodeConfig.Instance.AuthorityOrigin)
                ? CommunityNodeTokenService.NormalizeOrigin(NodeConfig.Instance.AuthorityOrigin)
                : origin;

            return new NodeManifest
            {
                NodeId = CommunityNodeTokenService.ResolveNodeId(),
                Name = NodeConfig.Instance.Name,
                CanonicalOrigin = origin,
                AuthorityOrigin = authorityOrigin,
                Version = service.Version,
                Mode = NodeConfig.Instance.Mode,
                PlanetIds = hostedService.GetHostedIds()
            };
        }
    }
}

