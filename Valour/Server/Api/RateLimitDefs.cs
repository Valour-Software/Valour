using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Valour.Server.API;

public static class RateLimitDefs
{
    public static void AddRateLimitDefs(IServiceCollection services)
    {
        services.AddRateLimiter(_ =>
        {
            // Login attempts - strict limit
            _.AddFixedWindowLimiter("login", options =>
            {
                options.PermitLimit = 5;
                options.Window = TimeSpan.FromSeconds(60);
                options.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                options.QueueLimit = 2;
            });

            // Registration - prevent spam account creation (per-IP)
            _.AddPolicy("register", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 3,
                        Window = TimeSpan.FromMinutes(10),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 1,
                    }));

            // Password reset - prevent email spam and enumeration attacks
            _.AddFixedWindowLimiter("password-reset", options =>
            {
                options.PermitLimit = 3;
                options.Window = TimeSpan.FromMinutes(15);
                options.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                options.QueueLimit = 1;
            });

            // Email verification - prevent brute force code guessing
            _.AddFixedWindowLimiter("email-verify", options =>
            {
                options.PermitLimit = 10;
                options.Window = TimeSpan.FromMinutes(5);
                options.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                options.QueueLimit = 2;
            });

            // MFA operations - prevent brute force code guessing
            _.AddFixedWindowLimiter("mfa", options =>
            {
                options.PermitLimit = 5;
                options.Window = TimeSpan.FromMinutes(5);
                options.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                options.QueueLimit = 1;
            });

            // Password change - prevent brute force
            _.AddFixedWindowLimiter("password-change", options =>
            {
                options.PermitLimit = 3;
                options.Window = TimeSpan.FromMinutes(10);
                options.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                options.QueueLimit = 1;
            });

            // OAuth token exchange - prevent brute force
            _.AddFixedWindowLimiter("oauth", options =>
            {
                options.PermitLimit = 10;
                options.Window = TimeSpan.FromMinutes(5);
                options.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                options.QueueLimit = 2;
            });

            _.OnRejected = async (OnRejectedContext ctx, CancellationToken token) =>
            {
                ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await ctx.HttpContext.Response.WriteAsync("Too many requests. Please wait a moment and try again.", token);
                Console.WriteLine("Rate limit exceeded for " + ctx.HttpContext.Connection.RemoteIpAddress + " on " + ctx.HttpContext.Request.Path);
            };
        });
    }
}