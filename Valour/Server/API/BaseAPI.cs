
using Valour.Client.Planets;

namespace Valour.Server.API;
public class BaseAPI
{
    public static void AddRoutes(WebApplication app)
    {
        app.MapGet("api/version", () => new ClientPlanet().GetType().Assembly.GetName().Version.ToString());
    }

    public static async Task TokenInvalid(HttpContext ctx)
    {
        ctx.Response.StatusCode = 401;
        await ctx.Response.WriteAsync($"Token is invalid.");
    }

    public static async Task Unauthorized(string reason, HttpContext ctx)
    {
        ctx.Response.StatusCode = 403;
        await ctx.Response.WriteAsync(reason);
    }

    public static async Task NotFound(string reason, HttpContext ctx)
    {
        ctx.Response.StatusCode = 404;
        await ctx.Response.WriteAsync(reason);
    }

    public static async Task BadRequest(string reason, HttpContext ctx)
    {
        ctx.Response.StatusCode = 400;
        await ctx.Response.WriteAsync(reason);
    }
}
