using System.Net.Http.Json;
using StackExchange.Redis;
using Valour.Config.Configs;
using Valour.Server.Api.Dynamic;
using Valour.Server.Redis;
using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Server.Services;

/// <summary>
/// Node-side honoring of account-deletion purges. Pulls deletion tombstones
/// from the hub and hard-deletes those users' local (federated shadow) data,
/// so a deleted account is removed from community nodes too. Idempotent — a
/// user already absent locally is skipped.
/// </summary>
public class FederationPurgeService
{
    private const string CursorKeyPrefix = "federation:purge:cursor";

    private readonly ValourDb _db;
    private readonly UserService _userService;
    private readonly FederationNodeService _nodeService;
    private readonly FederationKeyService _keyService;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<FederationPurgeService> _logger;

    public FederationPurgeService(
        ValourDb db,
        UserService userService,
        FederationNodeService nodeService,
        FederationKeyService keyService,
        IHttpClientFactory httpFactory,
        IConnectionMultiplexer redis,
        ILogger<FederationPurgeService> logger)
    {
        _db = db;
        _userService = userService;
        _nodeService = nodeService;
        _keyService = keyService;
        _httpFactory = httpFactory;
        _redis = redis;
        _logger = logger;
    }

    /// <summary>
    /// Pulls recent purges from the hub and applies them locally. Returns the
    /// number of local accounts purged.
    /// </summary>
    public async Task<int> HonorPurgesAsync(TimeSpan lookback)
    {
        if (!FederationNodeService.NodeEnabled)
            return 0;

        // Durable monotonic cursor: resume exactly where this node left off.
        // The caller's lookback remains in the signature for compatibility with
        // the worker, but cursor delivery deliberately has no time-window gap.
        // A cursor belongs to the hub that issued it. Reusing it after an
        // operator repoints a node at a different hub can permanently skip
        // that hub's older deletion tombstones (its ids commonly start below
        // the previous hub's cursor).
        var hubUrl = FederationConfig.Current.HubUrl.TrimEnd('/');
        var cursorKey = GetCursorKey(hubUrl);
        var cursorDb = _redis.GetDatabase(RedisDbTypes.Cluster);
        var cursorRaw = await cursorDb.StringGetAsync(cursorKey);
        var cursor = cursorRaw.HasValue && long.TryParse(cursorRaw.ToString(), out var storedCursor)
            ? Math.Max(0, storedCursor)
            : 0;

        var purged = 0;
        while (true)
        {
            // A full backlog can span the five-minute S2S token lifetime.
            // Refresh per page so a later request does not fail merely because
            // the cursor catch-up was successful but slow.
            var nodeToken = await _nodeService.MintS2STokenAsync(_keyService);
            if (nodeToken is null)
            {
                _logger.LogWarning("Cannot pull purges: node signing key unavailable");
                return purged;
            }

            FederatedPurgePage page;
            try
            {
                var client = _httpFactory.CreateClient("federation");
                var url = hubUrl + $"/api/federation/purges?after={cursor}";
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add(FederationApi.NodeAuthHeader, nodeToken);
                using var response = await client.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Purge pull rejected by hub: {Status}", (int)response.StatusCode);
                    return purged;
                }

                page = await response.Content.ReadFromJsonAsync<FederatedPurgePage>();
                if (page is null || page.NextCursor < cursor)
                {
                    _logger.LogWarning("Hub returned an invalid federation purge cursor");
                    return purged;
                }
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Failed to pull purges from hub");
                return purged;
            }

            foreach (var id in page.UserIds.Distinct())
            {
                // Only purge local federated shadow accounts — never a real local user.
                var user = await _db.Users.FindAsync(id);
                if (user is null || !user.IsFederated)
                    continue;

                var result = await _userService.HardDelete(user.ToModel());
                if (!result.Success)
                {
                    // Do not advance this page's cursor. A retry is idempotent and
                    // prevents a failed local deletion from being lost forever.
                    _logger.LogWarning("Failed to purge federated user {UserId}: {Message}", id, result.Message);
                    return purged;
                }

                purged++;
            }

            // Advance only after every record in the page was handled.
            if (page.NextCursor > cursor)
                await cursorDb.StringSetAsync(cursorKey, page.NextCursor.ToString());

            if (page.UserIds.Count == 0 || page.NextCursor == cursor)
                break;

            cursor = page.NextCursor;
        }

        if (purged > 0)
            _logger.LogInformation("Honored {Count} account-deletion purges", purged);

        return purged;
    }

    private static string GetCursorKey(string hubUrl) =>
        $"{CursorKeyPrefix}:{hubUrl.ToLowerInvariant()}";
}
