using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Valour.Server.API;

public static class RateLimitDefs
{
    /// <summary>
    /// Helper to create a per-IP fixed window rate limiter policy.
    /// </summary>
    private static Func<HttpContext, RateLimitPartition<string>> PerIpFixedWindow(
        int permitLimit, TimeSpan window, int queueLimit = 1)
    {
        return context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = window,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = queueLimit,
            });
    }

    public static void AddRateLimitDefs(IServiceCollection services)
    {
        services.AddRateLimiter(_ =>
        {
            // Login attempts - strict limit (per-IP)
            _.AddPolicy("login", PerIpFixedWindow(
                permitLimit: 5, window: TimeSpan.FromSeconds(60), queueLimit: 2));

            // Registration - prevent spam account creation (per-IP)
            _.AddPolicy("register", PerIpFixedWindow(
                permitLimit: 3, window: TimeSpan.FromMinutes(10)));

            // Password reset - prevent email spam and enumeration attacks (per-IP)
            _.AddPolicy("password-reset", PerIpFixedWindow(
                permitLimit: 3, window: TimeSpan.FromMinutes(15)));

            // Email verification - prevent brute force code guessing (per-IP)
            _.AddPolicy("email-verify", PerIpFixedWindow(
                permitLimit: 10, window: TimeSpan.FromMinutes(5), queueLimit: 2));

            // MFA operations - prevent brute force code guessing (per-IP)
            _.AddPolicy("mfa", PerIpFixedWindow(
                permitLimit: 5, window: TimeSpan.FromMinutes(5)));

            // Password change - prevent brute force (per-IP)
            _.AddPolicy("password-change", PerIpFixedWindow(
                permitLimit: 3, window: TimeSpan.FromMinutes(10)));

            // OAuth token exchange - prevent brute force (per-IP)
            _.AddPolicy("oauth", PerIpFixedWindow(
                permitLimit: 10, window: TimeSpan.FromMinutes(5), queueLimit: 2));

            _.OnRejected = async (OnRejectedContext ctx, CancellationToken token) =>
            {
                ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await ctx.HttpContext.Response.WriteAsync("Too many requests. Please wait a moment and try again.", token);
                Console.WriteLine("Rate limit exceeded for " + ctx.HttpContext.Connection.RemoteIpAddress + " on " + ctx.HttpContext.Request.Path);
            };
        });
    }
}
