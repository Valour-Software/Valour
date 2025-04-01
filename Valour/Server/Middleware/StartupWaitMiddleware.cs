using Valour.Server.Utilities;

namespace Valour.Server.Middleware;

using Microsoft.AspNetCore.Http;

public class StartupWaitMiddleware
{
    private readonly RequestDelegate _next;

    public StartupWaitMiddleware(
        RequestDelegate next,
        ILogger<StartupWaitMiddleware> logger)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (StartupWaitFlag.Value > 0)
        {
            // Allow js and css files (for building minified bundle)
            if (context.Request.Path.Value is not null && (
                context.Request.Path.Value.Contains(".js") || 
                context.Request.Path.Value.Contains(".css")))
            {
                // Don't serve bundled css until server is ready
                if (!context.Request.Path.Value.Contains("bundled.min.css"))
                {
                    await _next(context);
                    return;
                }
            }
            
            var startTime = DateTime.UtcNow;
            
            while (StartupWaitFlag.Value > 0)
            {
                if ((DateTime.UtcNow - startTime).Milliseconds > 30000)
                {
                    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    return;
                }

                await Task.Delay(1000); // Wait 1000ms before checking again
            }
        }

        await _next(context);
    }
}

// Extension method for easier registration
public static class StartupWaitMiddlewareExtensions
{
    public static IApplicationBuilder UseStartupWait(
        this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<StartupWaitMiddleware>();
    }
}
