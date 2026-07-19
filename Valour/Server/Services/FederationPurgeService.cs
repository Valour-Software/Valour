using System.Net.Http.Json;
using StackExchange.Redis;
using Valour.Config.Configs;
using Valour.Server.Api.Dynamic;
using Valour.Server.Redis;
using Valour.Shared;

namespace Valour.Server.Services;

/// <summary>
/// Node-side honoring of account-deletion purges. Pulls deletion tombstones
/// from the hub and hard-deletes those users' local (federated shadow) data,
/// so a deleted account is removed from community nodes too. Idempotent — a
/// user already absent locally is skipped.
/// </summary>
public class FederationPurgeService
{
    private const string CursorKey = "federation:purge:cursor";
    private static readonly TimeSpan CursorOverlap = TimeSpan.FromHours(1);

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

        // The purge list is privacy-sensitive (deleted account ids), so the hub
        // requires node authentication. Attach this node's self-signed S2S token —
        // without it every request is 403'd and silently retried forever.
        var nodeToken = await _nodeService.MintS2STokenAsync(_keyService);
        if (nodeToken is null)
        {
            _logger.LogWarning("Cannot pull purges: node signing key unavailable");
            return 0;
        }

        // Durable cursor: resume from the last watermark we processed so a node
        // that was offline longer than the poll lookback still catches every
        // tombstone. Falls back to the lookback window only when no cursor exists.
        var cursorDb = _redis.GetDatabase(RedisDbTypes.Cluster);
        var sinceTime = DateTime.UtcNow - lookback;
        var cursorRaw = await cursorDb.StringGetAsync(CursorKey);
        if (cursorRaw.HasValue && DateTime.TryParse((string)cursorRaw, null,
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var cursor))
        {
            // Overlap a little so nothing straddling the boundary is dropped.
            sinceTime = cursor - CursorOverlap;
        }

        // Stamp the watermark BEFORE fetching, so purges created during this run
        // are re-seen next time rather than skipped.
        var newCursor = DateTime.UtcNow;

        List<long> userIds;
        try
        {
            var client = _httpFactory.CreateClient("federation");
            var since = Uri.EscapeDataString(sinceTime.ToString("O"));
            var url = FederationConfig.Current.HubUrl.TrimEnd('/') + $"/api/federation/purges?since={since}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add(FederationApi.NodeAuthHeader, nodeToken);
            using var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Purge pull rejected by hub: {Status}", (int)response.StatusCode);
                return 0;
            }

            userIds = await response.Content.ReadFromJsonAsync<List<long>>() ?? new List<long>();
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Failed to pull purges from hub");
            return 0;
        }

        var purged = 0;
        foreach (var id in userIds.Distinct())
        {
            // Only purge local federated shadow accounts — never a real local user.
            var user = await _db.Users.FindAsync(id);
            if (user is null || !user.IsFederated)
                continue;

            var result = await _userService.HardDelete(user.ToModel());
            if (result.Success)
                purged++;
            else
                _logger.LogWarning("Failed to purge federated user {UserId}: {Message}", id, result.Message);
        }

        // Advance the durable cursor only after a clean pass (the fetch succeeded
        // and every applicable tombstone was applied), so a mid-run failure re-tries
        // the same window next time instead of skipping it.
        await cursorDb.StringSetAsync(CursorKey, newCursor.ToString("O"));

        if (purged > 0)
            _logger.LogInformation("Honored {Count} account-deletion purges", purged);

        return purged;
    }
}
