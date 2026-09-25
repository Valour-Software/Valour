using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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
    /// End-to-end encryption routes that make the server compute channel
    /// viewers, verify key logs, or scan search terms: key requests, new key
    /// generations, key candidates, member lists, search, and key sharing.
    /// Limited per account rather than per address, since many users can
    /// share an address.
    /// </summary>
    public const string E2ee = "e2ee";

    /// <summary>
    /// End-to-end encryption routes that clients call often while reading:
    /// channel keys, key logs, key holders, older key records, access logs,
    /// sealed-history indexing, automod term work, and removing the caller's
    /// own boxes. The limit is far above normal use and stops a client that
    /// loops. Limited per account, like <see cref="E2ee"/>.
    /// </summary>
    public const string E2eeRead = "e2ee-read";

    /// <summary>
    /// Filing reports. A report can carry several megabytes of message
    /// evidence, and people file very few, so the limit is low. Limited per
    /// account, like <see cref="E2ee"/>.
    /// </summary>
    public const string Report = "report";

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

        // Partitioning depends on how much the resolver trusts proxy headers,
        // so its configuration is applied alongside the policies that use it.
        ClientAddressResolver.Configure(configuration);

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
            AddPerAccountWindow(options, E2ee, permitLimit: 300, window: TimeSpan.FromMinutes(1), enabled);
            AddPerAccountWindow(options, E2eeRead, permitLimit: 1200, window: TimeSpan.FromMinutes(1), enabled);
            AddPerAccountWindow(options, Report, permitLimit: 10, window: TimeSpan.FromMinutes(10), enabled);
        });
    }

    // The account behind each auth token the per-account policies have seen,
    // by token hash. Zero while the lookup runs or when the token is unknown.
    private static readonly ConcurrentDictionary<string, long> TokenUsers = new();
    private const int MaxTokenUsers = 100_000;
    private const int MaxPendingLookups = 64;
    private static int _pendingLookups;

    /// <summary>
    /// Partitions by account, so signing in on more devices does not raise
    /// the limit. The limiter runs before authentication, so the first
    /// requests with a new token are counted under the token (hashed, so the
    /// limiter does not keep it) while its account is looked up in the
    /// background. Requests without a token are counted by client address.
    /// </summary>
    private static void AddPerAccountWindow(
        RateLimiterOptions options, string policyName, int permitLimit, TimeSpan window, bool enabled)
    {
        options.AddPolicy(policyName, context =>
        {
            if (!enabled)
                return RateLimitPartition.GetNoLimiter("disabled");

            return RateLimitPartition.GetFixedWindowLimiter(
                GetAccountPartition(context),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitLimit,
                    Window = window,
                    QueueLimit = 0,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                });
        });
    }

    private static string GetAccountPartition(HttpContext context)
    {
        var token = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(token))
            return "ip:" + ClientAddressResolver.GetClientAddress(context);

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)), 0, 16);
        if (TokenUsers.TryGetValue(hash, out var userId))
            return userId > 0 ? "user:" + userId.ToString(CultureInfo.InvariantCulture) : "token:" + hash;

        LookUpTokenUser(context.RequestServices, token, hash);
        return "token:" + hash;
    }

    private static void LookUpTokenUser(IServiceProvider services, string token, string hash)
    {
        if (Interlocked.Increment(ref _pendingLookups) > MaxPendingLookups)
        {
            Interlocked.Decrement(ref _pendingLookups);
            return;
        }

        Valour.Server.Services.E2eeCacheLimit.Trim(TokenUsers, MaxTokenUsers);
        if (!TokenUsers.TryAdd(hash, 0))
        {
            Interlocked.Decrement(ref _pendingLookups);
            return;
        }

        var scopeFactory = services.GetRequiredService<IServiceScopeFactory>();
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
                TokenUsers[hash] = await db.AuthTokens.AsNoTracking()
                    .Where(x => x.Id == token)
                    .Select(x => x.UserId)
                    .FirstOrDefaultAsync();
            }
            catch
            {
                // Tried again on the token's next request.
                TokenUsers.TryRemove(hash, out _);
            }
            finally
            {
                Interlocked.Decrement(ref _pendingLookups);
            }
        });
    }

    /// <summary>
    /// Partitions by client address (IPv6 by /64). ClientAddressResolver only
    /// trusts proxy headers when the socket peer is a local reverse proxy, so
    /// this cannot be escaped by spoofing X-Forwarded-For on a direct connection.
    /// </summary>
    private static void AddFixedWindow(
        RateLimiterOptions options, string policyName, int permitLimit, TimeSpan window, bool enabled)
    {
        options.AddPolicy(policyName, context =>
        {
            if (!enabled)
                return RateLimitPartition.GetNoLimiter("disabled");

            return RateLimitPartition.GetFixedWindowLimiter(
                ClientAddressResolver.GetRateLimitKey(context),
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
