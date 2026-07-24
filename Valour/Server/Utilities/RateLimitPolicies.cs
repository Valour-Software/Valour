using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Valour.Server.Utilities;

/// <summary>
/// Application-level rate limiting for the endpoints that are cheap to call and
/// expensive to serve, or that leak information when hammered.
///
/// The edge (Cloudflare) already absorbs volumetric abuse, but this is the
/// defense-in-depth layer for anything that reaches the origin directly, and it
/// is what bounds per-account brute force rather than per-IP flooding.
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>
    /// Credential verification (login, OAuth token exchange). Each attempt
    /// burns a PBKDF2 hash, so this is CPU amplification as well as a brute
    /// force vector.
    /// </summary>
    public const string Auth = "auth";

    /// <summary>
    /// Endpoints that cause an email to be sent. Abuse here spends money and
    /// burns sender reputation, and lets an attacker mailbomb a third party.
    /// </summary>
    public const string Email = "email";

    /// <summary>
    /// Account creation.
    /// </summary>
    public const string Register = "register";

    /// <summary>
    /// Set to true to make every policy a no-op. Only for the test host, which
    /// drives hundreds of registrations and logins from a single address.
    /// The policies are still registered either way, because
    /// RequireRateLimiting throws at startup for an unknown policy name.
    /// </summary>
    public const string DisabledSetting = "RateLimiting:Disabled";

    public static void AddValourRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var enabled = !configuration.GetValue<bool>(DisabledSetting);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString(NumberFormatInfo.InvariantInfo);
                }

                await context.HttpContext.Response.WriteAsync(
                    "Too many requests. Please slow down and try again shortly.", cancellationToken);
            };

            AddFixedWindow(options, Auth, permitLimit: 10, window: TimeSpan.FromMinutes(1), enabled);
            AddFixedWindow(options, Email, permitLimit: 4, window: TimeSpan.FromMinutes(10), enabled);
            AddFixedWindow(options, Register, permitLimit: 3, window: TimeSpan.FromMinutes(10), enabled);
        });
    }

    /// <summary>
    /// Partitions by client address. ClientAddressResolver only trusts proxy
    /// headers when the socket peer is a local reverse proxy, so this cannot be
    /// escaped by spoofing X-Forwarded-For on a direct connection.
    /// </summary>
    private static void AddFixedWindow(
        RateLimiterOptions options, string policyName, int permitLimit, TimeSpan window, bool enabled)
    {
        options.AddPolicy(policyName, context =>
        {
            if (!enabled)
                return RateLimitPartition.GetNoLimiter("disabled");

            return RateLimitPartition.GetFixedWindowLimiter(
                ClientAddressResolver.GetClientAddress(context),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitLimit,
                    Window = window,
                    QueueLimit = 0,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                });
        });
    }
}
