using System.Net.Http.Json;
using Valour.Config.Configs;
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
    private readonly ValourDb _db;
    private readonly UserService _userService;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<FederationPurgeService> _logger;

    public FederationPurgeService(
        ValourDb db,
        UserService userService,
        IHttpClientFactory httpFactory,
        ILogger<FederationPurgeService> logger)
    {
        _db = db;
        _userService = userService;
        _httpFactory = httpFactory;
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

        List<long> userIds;
        try
        {
            var client = _httpFactory.CreateClient("federation");
            var since = Uri.EscapeDataString((DateTime.UtcNow - lookback).ToString("O"));
            var url = FederationConfig.Current.HubUrl.TrimEnd('/') + $"/api/federation/purges?since={since}";
            userIds = await client.GetFromJsonAsync<List<long>>(url) ?? new List<long>();
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

        if (purged > 0)
            _logger.LogInformation("Honored {Count} account-deletion purges", purged);

        return purged;
    }
}
