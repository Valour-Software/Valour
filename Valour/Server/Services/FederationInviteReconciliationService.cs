using System.Net.Http.Json;
using Valour.Config.Configs;
using Valour.Server.Api.Dynamic;
using Valour.Shared.Models;

namespace Valour.Server.Services;

/// <summary>
/// Delivers node-side offline invite redemptions to the hub. Rejections are
/// fail-closed: the node removes the local federated membership instead of
/// allowing an outage-created grant to become permanent without hub consent.
/// </summary>
public class FederationInviteReconciliationService
{
    private const int BatchSize = 50;

    private readonly ValourDb _db;
    private readonly FederationNodeService _nodeService;
    private readonly FederationKeyService _keyService;
    private readonly PlanetMemberService _memberService;
    private readonly TokenService _tokenService;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<FederationInviteReconciliationService> _logger;

    public FederationInviteReconciliationService(
        ValourDb db,
        FederationNodeService nodeService,
        FederationKeyService keyService,
        PlanetMemberService memberService,
        TokenService tokenService,
        IHttpClientFactory httpFactory,
        ILogger<FederationInviteReconciliationService> logger)
    {
        _db = db;
        _nodeService = nodeService;
        _keyService = keyService;
        _memberService = memberService;
        _tokenService = tokenService;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public async Task<int> SyncPendingAsync()
    {
        if (!FederationNodeService.NodeEnabled)
            return 0;

        var pending = await _db.FederatedInviteRedemptions
            .Where(x => x.ReportedAt == null && x.RejectedAt == null)
            .OrderBy(x => x.RedeemedAt)
            .Take(BatchSize)
            .ToListAsync();
        if (pending.Count == 0)
            return 0;

        var synced = 0;
        foreach (var redemption in pending)
        {
            // A batch can take longer than the five-minute S2S lifetime when
            // the hub is slow. Mint per receipt so an expired batch token is
            // never mistaken for a definitive 401/revocation below.
            var nodeToken = await _nodeService.MintS2STokenAsync(_keyService);
            if (nodeToken is null)
            {
                _logger.LogWarning("Cannot reconcile federation invite {GrantId}: node signing key unavailable", redemption.GrantId);
                break;
            }

            var report = new FederatedInviteRedemptionReport
            {
                GrantId = redemption.GrantId,
                UserId = redemption.UserId,
                PlanetId = redemption.PlanetId,
                RedeemedAt = redemption.RedeemedAt,
                Passport = redemption.Passport,
                Proof = redemption.Proof,
            };

            HttpResponseMessage response;
            try
            {
                var client = _httpFactory.CreateClient("federation");
                var url = FederationConfig.Current.HubUrl.TrimEnd('/') + "/api/federation/invites/redemptions";
                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = JsonContent.Create(report),
                };
                request.Headers.Add(FederationApi.NodeAuthHeader, nodeToken);
                response = await client.SendAsync(request);
            }
            catch (Exception e)
            {
                _logger.LogInformation(e, "Federation invite reconciliation is offline");
                break;
            }

            using (response)
            {
                if (response.IsSuccessStatusCode)
                {
                    redemption.ReportedAt = DateTime.UtcNow;
                    // The node no longer needs bearer-ish artifacts once the
                    // hub independently persisted the membership.
                    redemption.Passport = null;
                    redemption.Proof = null;
                    await _db.SaveChangesAsync();
                    synced++;
                    continue;
                }

                // A 4xx from the authenticated hub is a definitive refusal:
                // do not keep retrying a revoked, expired, or malformed grant.
                if ((int)response.StatusCode is >= 400 and <= 499)
                {
                    redemption.RejectedAt = DateTime.UtcNow;
                    redemption.RejectionReason = await response.Content.ReadAsStringAsync();
                    redemption.Passport = null;
                    redemption.Proof = null;
                    await _db.SaveChangesAsync();

                    var memberId = await _db.PlanetMembers
                        .Where(x => x.UserId == redemption.UserId && x.PlanetId == redemption.PlanetId)
                        .Select(x => x.Id)
                        .FirstOrDefaultAsync();
                    if (memberId != 0)
                    {
                        var removal = await _memberService.DeleteAsync(memberId, bypassMigrationLock: true);
                        if (!removal.Success)
                            _logger.LogWarning("Failed to revoke rejected federation invite member {MemberId}: {Message}", memberId, removal.Message);
                    }

                    // Membership removal alone is not enough: an already issued
                    // node-local bearer would otherwise retain unrelated API
                    // access until its natural expiry.
                    var tokenIds = await _db.AuthTokens
                        .Where(x => x.AppId == "FEDERATION" && x.UserId == redemption.UserId)
                        .Select(x => x.Id)
                        .ToListAsync();
                    if (tokenIds.Count > 0)
                    {
                        await _db.AuthTokens.Where(x => tokenIds.Contains(x.Id)).ExecuteDeleteAsync();
                        foreach (var tokenId in tokenIds)
                            _tokenService.RemoveFromQuickCache(tokenId);
                    }

                    continue;
                }

                _logger.LogWarning("Hub returned {Status} while reconciling federation invite {GrantId}",
                    (int)response.StatusCode, redemption.GrantId);
                break;
            }
        }

        return synced;
    }
}
