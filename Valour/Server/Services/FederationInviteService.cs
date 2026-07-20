using System.Security.Cryptography;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Valour.Config.Configs;
using Valour.Database;
using Valour.Server.Api.Dynamic;
using Valour.Shared;
using Valour.Shared.Models;
using ServerAuthToken = Valour.Server.Models.AuthToken;

namespace Valour.Server.Services;

/// <summary>
/// Hub-issued, node-redeemable federation invitations. The hub signs each
/// recipient-bound grant; the destination node can verify it from cached hub
/// keys and therefore does not need hub availability on the join path.
/// </summary>
public class FederationInviteService
{
    private static readonly TimeSpan MaximumGrantLifetime = TimeSpan.FromDays(30);
    private static readonly TimeSpan PassportLifetime = TimeSpan.FromDays(7);
    private static readonly TimeSpan ProofClockSkew = TimeSpan.FromMinutes(2);
    // This must stay aligned with FederationNodeService's stale-JWKS window.
    // A node may only accept an invite while it has recently verified the
    // hub's keys; its later report cannot backdate a redemption indefinitely.
    private static readonly TimeSpan MaximumOfflineRedemptionAge = TimeSpan.FromMinutes(15);

    private readonly ValourDb _db;
    private readonly FederationKeyService _keyService;
    private readonly FederationNodeService _nodeService;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<FederationInviteService> _logger;

    public FederationInviteService(
        ValourDb db,
        FederationKeyService keyService,
        FederationNodeService nodeService,
        IHttpClientFactory httpFactory,
        ILogger<FederationInviteService> logger)
    {
        _db = db;
        _keyService = keyService;
        _nodeService = nodeService;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    /// <summary>
    /// Issues a short-lived identity passport bound to a client public key.
    /// Possession of this JWT alone is insufficient to redeem an invite: the
    /// client must also sign a proof over the invite's complete authorization
    /// scope.
    /// </summary>
    public async Task<TaskResult<FederationPassportResponse>> MintPassportAsync(
        Valour.Server.Models.User user,
        string publicJwk)
    {
        if (!FederationHubService.HubEnabled)
            return TaskResult<FederationPassportResponse>.FromFailure("Community-server features are only available on the official server.");

        if (!IsValidClientPublicJwk(publicJwk))
            return TaskResult<FederationPassportResponse>.FromFailure("A P-256 client public key is required.");

        var credentials = await _keyService.GetHubSigningCredentialsAsync();
        if (credentials is null)
            return TaskResult<FederationPassportResponse>.FromFailure("Hub signing key unavailable.");

        var now = DateTime.UtcNow;
        var expires = now.Add(PassportLifetime);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = FederationHubService.Issuer,
            Audience = ValourFederation.PassportAudience,
            Expires = expires,
            IssuedAt = now,
            SigningCredentials = credentials,
            Claims = new Dictionary<string, object>
            {
                ["sub"] = user.Id.ToString(),
                ["name"] = user.Name,
                ["subscription"] = user.SubscriptionType ?? string.Empty,
                ["protocol"] = ValourFederation.ProtocolVersion,
                ["purpose"] = ValourFederation.PassportPurpose,
                ["jti"] = Guid.NewGuid().ToString("N"),
                ["cnf"] = publicJwk,
            },
        };

        return TaskResult<FederationPassportResponse>.FromData(new FederationPassportResponse
        {
            Token = new JsonWebTokenHandler().CreateToken(descriptor),
            ExpiresAt = expires,
        });
    }

    /// <summary>Creates a recipient-bound, hub-signed offline invite grant.</summary>
    public async Task<TaskResult<FederatedInviteGrantResponse>> CreateAsync(
        long creatorUserId,
        FederatedInviteGrantCreateRequest request)
    {
        if (!FederationHubService.HubEnabled)
            return TaskResult<FederatedInviteGrantResponse>.FromFailure("Community-server features are only available on the official server.");

        if (request is null || request.PlanetId <= 0 || request.IntendedUserId <= 0)
            return TaskResult<FederatedInviteGrantResponse>.FromFailure("Planet and recipient are required.");

        // v2 grants name one recipient and are deliberately single-use. A
        // shareable multi-use capability would let a node replay one observed
        // passport for another recipient without a stronger client key model.
        if (request.MaxUses != 1)
            return TaskResult<FederatedInviteGrantResponse>.FromFailure("Recipient-bound federation invites can be redeemed once.");

        var now = DateTime.UtcNow;
        if (request.ExpiresAt <= now || request.ExpiresAt > now.Add(MaximumGrantLifetime))
            return TaskResult<FederatedInviteGrantResponse>.FromFailure("Invite expiry must be within the next 30 days.");

        var stub = await _db.FederatedPlanetStubs.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.PlanetId);
        if (stub is null)
            return TaskResult<FederatedInviteGrantResponse>.FromFailure("Community-hosted planet not found.");
        if (stub.OwnerId != creatorUserId)
            return TaskResult<FederatedInviteGrantResponse>.FromFailure("Only the planet owner can issue federation invites.");

        var nodeActive = await _db.FederatedNodes.AsNoTracking()
            .AnyAsync(x => x.Domain == stub.NodeDomain && x.Status == FederatedNodeStatus.Active);
        if (!nodeActive)
            return TaskResult<FederatedInviteGrantResponse>.FromFailure("The community node is unavailable.");

        if (!await _db.Users.AsNoTracking().AnyAsync(x => x.Id == request.IntendedUserId))
            return TaskResult<FederatedInviteGrantResponse>.FromFailure("Invite recipient not found.");

        var credentials = await _keyService.GetHubSigningCredentialsAsync();
        if (credentials is null)
            return TaskResult<FederatedInviteGrantResponse>.FromFailure("Hub signing key unavailable.");

        var id = Guid.NewGuid().ToString("N");
        var grant = new FederatedInviteGrant
        {
            Id = id,
            PlanetId = stub.Id,
            NodeDomain = stub.NodeDomain,
            CreatorUserId = creatorUserId,
            IntendedUserId = request.IntendedUserId,
            MaxUses = request.MaxUses,
            Uses = 0,
            CreatedAt = now,
            ExpiresAt = request.ExpiresAt.ToUniversalTime(),
        };

        await _db.FederatedInviteGrants.AddAsync(grant);
        await _db.SaveChangesAsync();

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = FederationHubService.Issuer,
            Audience = grant.NodeDomain,
            Expires = grant.ExpiresAt,
            IssuedAt = now,
            SigningCredentials = credentials,
            Claims = new Dictionary<string, object>
            {
                ["jti"] = grant.Id,
                ["purpose"] = ValourFederation.InvitePurpose,
                ["planet_id"] = grant.PlanetId.ToString(),
                ["node_domain"] = grant.NodeDomain,
                ["recipient_id"] = grant.IntendedUserId.ToString(),
                ["max_uses"] = grant.MaxUses,
                ["protocol"] = ValourFederation.ProtocolVersion,
            },
        };

        return TaskResult<FederatedInviteGrantResponse>.FromData(new FederatedInviteGrantResponse
        {
            Grant = new JsonWebTokenHandler().CreateToken(descriptor),
            GrantId = grant.Id,
            PlanetId = grant.PlanetId,
            NodeDomain = grant.NodeDomain,
            IntendedUserId = grant.IntendedUserId,
            ExpiresAt = grant.ExpiresAt,
        });
    }

    public async Task<TaskResult> RevokeAsync(long creatorUserId, string grantId)
    {
        if (!FederationHubService.HubEnabled)
            return TaskResult.FromFailure("Community-server features are only available on the official server.");

        var grant = await _db.FederatedInviteGrants.FindAsync(grantId);
        if (grant is null)
            return TaskResult.FromFailure("Invite grant not found.");
        if (grant.CreatorUserId != creatorUserId)
            return TaskResult.FromFailure("Only the invite creator can revoke it.");

        grant.RevokedAt ??= DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// Destination-node entry point. It confirms a redemption with the hub
    /// first whenever possible; bounded offline acceptance is used only when
    /// the hub cannot be reached.
    /// </summary>
    public async Task<TaskResult<ServerAuthToken>> RedeemOnNodeAsync(
        FederatedInviteRedeemRequest request,
        string issuedAddress)
    {
        if (!FederationNodeService.NodeEnabled)
            return TaskResult<ServerAuthToken>.FromFailure("This instance is not a community node.");
        if (request is null || string.IsNullOrWhiteSpace(request.Grant) ||
            string.IsNullOrWhiteSpace(request.Passport) || string.IsNullOrWhiteSpace(request.Proof))
            return TaskResult<ServerAuthToken>.FromFailure("Invite grant, passport, and proof are required.");

        var grantClaims = await _nodeService.ValidateHubSignedTokenAsync(
            request.Grant, FederationConfig.Current.NodeDomain, allowStaleKeys: true);
        var passportClaims = await _nodeService.ValidateHubSignedTokenAsync(
            request.Passport, ValourFederation.PassportAudience, allowStaleKeys: true);
        if (grantClaims is null || passportClaims is null ||
            !HasPurpose(grantClaims, ValourFederation.InvitePurpose) ||
            !HasPurpose(passportClaims, ValourFederation.PassportPurpose))
            return TaskResult<ServerAuthToken>.FromFailure("Invalid or expired federation invite.");

        if (!TryGetString(grantClaims, "jti", out var grantId) ||
            !TryGetLong(grantClaims, "planet_id", out var planetId) ||
            !TryGetString(grantClaims, "node_domain", out var nodeDomain) ||
            !TryGetLong(grantClaims, "recipient_id", out var intendedUserId) ||
            !TryGetInt(grantClaims, "max_uses", out var maxUses) ||
            !TryGetLong(passportClaims, "sub", out var userId) ||
            !TryGetString(passportClaims, "name", out var name) ||
            !TryGetString(passportClaims, "jti", out var passportId) ||
            !TryGetString(passportClaims, "cnf", out var publicJwk) ||
            !string.Equals(nodeDomain, FederationConfig.Current.NodeDomain, StringComparison.Ordinal) ||
            userId != intendedUserId || maxUses != 1)
            return TaskResult<ServerAuthToken>.FromFailure("Invalid federation invite claims.");

        if (!ValidateProof(
                request.Proof,
                publicJwk,
                grantId,
                nodeDomain,
                planetId,
                intendedUserId,
                maxUses,
                passportId,
                userId))
            return TaskResult<ServerAuthToken>.FromFailure("Invalid invite redemption proof.");

        var planetExists = await _db.Planets.AsNoTracking()
            .AnyAsync(x => x.Id == planetId && !x.IsDeleted);
        if (!planetExists)
            return TaskResult<ServerAuthToken>.FromFailure("This node does not host the invited planet.");

        var redeemedAt = DateTime.UtcNow;
        var reconciliation = await TryReconcileOnlineAsync(new FederatedInviteRedemptionReport
        {
            GrantId = grantId,
            UserId = userId,
            PlanetId = planetId,
            RedeemedAt = redeemedAt,
            Passport = request.Passport,
            Proof = request.Proof,
        });
        if (reconciliation == HubRedemptionResult.Rejected)
            return TaskResult<ServerAuthToken>.FromFailure("This invite is no longer valid.");

        await using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            // A transaction-scoped advisory lock makes MaxUses atomic even when
            // several invite redeems hit this node at the same instant.
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtext({grantId}))");

            var cachedGrant = await _db.FederatedInviteGrants.FindAsync(grantId);
            if (cachedGrant is null)
            {
                cachedGrant = new FederatedInviteGrant
                {
                    Id = grantId,
                    PlanetId = planetId,
                    NodeDomain = nodeDomain,
                    IntendedUserId = intendedUserId,
                    MaxUses = maxUses,
                    Uses = 0,
                    CreatedAt = redeemedAt,
                    ExpiresAt = ReadTokenExpiry(request.Grant),
                };
                await _db.FederatedInviteGrants.AddAsync(cachedGrant);
            }
            else if (cachedGrant.PlanetId != planetId || cachedGrant.NodeDomain != nodeDomain ||
                     cachedGrant.IntendedUserId != intendedUserId || cachedGrant.MaxUses != maxUses ||
                     cachedGrant.RevokedAt is not null || cachedGrant.ExpiresAt <= DateTime.UtcNow)
            {
                await transaction.RollbackAsync();
                return TaskResult<ServerAuthToken>.FromFailure("This invite is no longer valid on this node.");
            }

            var redemption = await _db.FederatedInviteRedemptions.FindAsync(grantId, userId);
            if (redemption is null)
            {
                if (cachedGrant.Uses >= cachedGrant.MaxUses)
                {
                    await transaction.RollbackAsync();
                    return TaskResult<ServerAuthToken>.FromFailure("This invite has reached its usage limit.");
                }

                cachedGrant.Uses++;
                redemption = new FederatedInviteRedemption
                {
                    GrantId = grantId,
                    UserId = userId,
                    PlanetId = planetId,
                    RedeemedAt = redeemedAt,
                    Passport = request.Passport,
                    Proof = request.Proof,
                };
                await _db.FederatedInviteRedemptions.AddAsync(redemption);
            }
            else if (redemption.RejectedAt is not null)
            {
                await transaction.RollbackAsync();
                return TaskResult<ServerAuthToken>.FromFailure("This invite redemption was rejected by the hub.");
            }

            if (reconciliation == HubRedemptionResult.Accepted)
            {
                redemption.ReportedAt ??= DateTime.UtcNow;
                redemption.Passport = null;
                redemption.Proof = null;
            }

            passportClaims.TryGetValue("subscription", out var subscriptionRaw);
            var session = await _nodeService.ProvisionInviteSessionAsync(
                userId, name, subscriptionRaw?.ToString(), planetId, issuedAddress);
            if (!session.Success)
            {
                await transaction.RollbackAsync();
                return session;
            }

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
            return session;
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Offline federation invite redemption failed for grant {GrantId}", grantId);
            await transaction.RollbackAsync();
            return TaskResult<ServerAuthToken>.FromFailure("Could not redeem the federation invite.");
        }
    }

    private enum HubRedemptionResult
    {
        Accepted,
        Rejected,
        Unavailable,
    }

    /// <summary>
    /// The hub remains the revocation authority. A 4xx is definitive and must
    /// fail closed; connection failures are the only path that may use the
    /// short offline-key window.
    /// </summary>
    private async Task<HubRedemptionResult> TryReconcileOnlineAsync(FederatedInviteRedemptionReport report)
    {
        var nodeToken = await _nodeService.MintS2STokenAsync(_keyService);
        if (nodeToken is null)
        {
            // This is a local trust failure, not evidence that the hub is
            // offline. Allowing cached-key redemption here would create an
            // avoidable revocation bypass whenever the node lost its own S2S
            // signing material and therefore cannot reconcile the receipt.
            _logger.LogWarning("Cannot preflight federation invite {GrantId}: node signing key unavailable", report.GrantId);
            return HubRedemptionResult.Rejected;
        }

        try
        {
            var client = _httpFactory.CreateClient("federation");
            var url = FederationConfig.Current.HubUrl.TrimEnd('/') + "/api/federation/invites/redemptions";
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(report),
            };
            request.Headers.Add(FederationApi.NodeAuthHeader, nodeToken);
            using var response = await client.SendAsync(request);
            if (response.IsSuccessStatusCode)
                return HubRedemptionResult.Accepted;

            // A response from the authenticated hub is authoritative. In
            // particular, treating 429 as an outage would let a caller turn a
            // deliberate rate limit into a fifteen-minute offline-redemption
            // window. Only a transport failure may use cached-key fallback.
            if ((int)response.StatusCode is >= 400 and <= 499)
                return HubRedemptionResult.Rejected;

            _logger.LogWarning("Hub returned {Status} while preflighting federation invite {GrantId}",
                (int)response.StatusCode, report.GrantId);
            return HubRedemptionResult.Unavailable;
        }
        catch (Exception e)
        {
            _logger.LogInformation(e, "Hub unavailable while preflighting federation invite {GrantId}", report.GrantId);
            return HubRedemptionResult.Unavailable;
        }
    }

    /// <summary>Hub-side verification of a delayed node redemption report.</summary>
    public async Task<TaskResult> ReconcileRedemptionAsync(string nodeDomain, FederatedInviteRedemptionReport report)
    {
        if (!FederationHubService.HubEnabled || report is null || string.IsNullOrWhiteSpace(report.GrantId))
            return TaskResult.FromFailure("Invalid federation redemption report.");

        // The authenticated node supplies RedeemedAt. Without a bounded age,
        // it could backdate a stored passport/proof to redeem a revoked or
        // expired invite long after the offline-key window ended.
        var now = DateTime.UtcNow;
        if (report.RedeemedAt < now.Subtract(MaximumOfflineRedemptionAge).Subtract(ProofClockSkew) ||
            report.RedeemedAt > now.Add(ProofClockSkew))
        {
            return TaskResult.FromFailure("Federation invite redemption was not reported within the allowed offline window.");
        }

        var grant = await _db.FederatedInviteGrants.FindAsync(report.GrantId);
        if (grant is null || grant.NodeDomain != nodeDomain || grant.PlanetId != report.PlanetId ||
            grant.IntendedUserId != report.UserId || grant.RevokedAt is not null || grant.ExpiresAt <= report.RedeemedAt)
            return TaskResult.FromFailure("The invite grant is no longer valid.");

        if (!await _db.Users.AsNoTracking().AnyAsync(x => x.Id == report.UserId))
            return TaskResult.FromFailure("The invite recipient no longer exists.");

        if (!await ValidateReportedProofAsync(report, grant))
            return TaskResult.FromFailure("The node did not provide a valid recipient proof.");

        await using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM federated_invite_grants WHERE id = {report.GrantId} FOR UPDATE");

            grant = await _db.FederatedInviteGrants.FindAsync(report.GrantId);
            if (grant is null || grant.RevokedAt is not null || grant.ExpiresAt <= report.RedeemedAt)
            {
                await transaction.RollbackAsync();
                return TaskResult.FromFailure("The invite grant is no longer valid.");
            }

            if (await _db.FederatedInviteRedemptions.AnyAsync(x => x.GrantId == report.GrantId && x.UserId == report.UserId))
            {
                await transaction.CommitAsync();
                return TaskResult.SuccessResult;
            }

            if (grant.Uses >= grant.MaxUses)
            {
                await transaction.RollbackAsync();
                return TaskResult.FromFailure("The invite grant has reached its usage limit.");
            }

            grant.Uses++;
            await _db.FederatedInviteRedemptions.AddAsync(new FederatedInviteRedemption
            {
                GrantId = report.GrantId,
                UserId = report.UserId,
                PlanetId = report.PlanetId,
                RedeemedAt = report.RedeemedAt,
                ReportedAt = DateTime.UtcNow,
            });

            if (!await _db.FederatedMemberships.AnyAsync(x => x.UserId == report.UserId && x.PlanetId == report.PlanetId))
            {
                await _db.FederatedMemberships.AddAsync(new FederatedMembership
                {
                    UserId = report.UserId,
                    PlanetId = report.PlanetId,
                    NodeDomain = nodeDomain,
                    JoinedAt = report.RedeemedAt,
                });
            }

            if (!await _db.FederatedAcceptedDomains.AnyAsync(x => x.UserId == report.UserId && x.Domain == nodeDomain))
            {
                await _db.FederatedAcceptedDomains.AddAsync(new FederatedAcceptedDomain
                {
                    UserId = report.UserId,
                    Domain = nodeDomain,
                    AcceptedAt = report.RedeemedAt,
                });
            }

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
            return TaskResult.SuccessResult;
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Failed to reconcile federation invite redemption {GrantId}", report.GrantId);
            await transaction.RollbackAsync();
            return TaskResult.FromFailure("Could not reconcile the federation invite redemption.");
        }
    }

    private async Task<bool> ValidateReportedProofAsync(FederatedInviteRedemptionReport report, FederatedInviteGrant grant)
    {
        var passport = await ValidateHubPassportAsync(report.Passport, validateLifetime: false);
        if (passport is null || !TryGetLong(passport, "sub", out var userId) || userId != grant.IntendedUserId ||
            !TryGetString(passport, "jti", out var passportId) || !TryGetString(passport, "cnf", out var publicJwk))
            return false;

        try
        {
            var passportToken = new JsonWebTokenHandler().ReadJsonWebToken(report.Passport);
            if (passportToken.ValidFrom > report.RedeemedAt.Add(ProofClockSkew) ||
                passportToken.ValidTo < report.RedeemedAt.Subtract(ProofClockSkew))
                return false;
        }
        catch
        {
            return false;
        }

        return ValidateProof(
            report.Proof,
            publicJwk,
            grant.Id,
            grant.NodeDomain,
            grant.PlanetId,
            grant.IntendedUserId,
            grant.MaxUses,
            passportId,
            userId);
    }

    private async Task<IDictionary<string, object>> ValidateHubPassportAsync(string token, bool validateLifetime)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        try
        {
            var jwks = new JsonWebKeySet(await _keyService.GetJwksJsonAsync());
            var validation = new TokenValidationParameters
            {
                ValidIssuer = FederationHubService.Issuer,
                ValidAudience = ValourFederation.PassportAudience,
                IssuerSigningKeys = jwks.GetSigningKeys(),
                ValidateIssuerSigningKey = true,
                ValidateLifetime = validateLifetime,
                ClockSkew = ProofClockSkew,
                ValidAlgorithms = new[] { SecurityAlgorithms.EcdsaSha256 },
                RequireSignedTokens = true,
            };
            var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, validation);
            return result.IsValid &&
                   HasPurpose(result.Claims, ValourFederation.PassportPurpose) &&
                   HasCurrentProtocol(result.Claims)
                ? result.Claims
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool ValidateProof(
        string proof,
        string publicJwk,
        string grantId,
        string nodeDomain,
        long planetId,
        long recipientUserId,
        int maxUses,
        string passportId,
        long userId)
    {
        if (!IsValidClientPublicJwk(publicJwk) || string.IsNullOrWhiteSpace(proof))
            return false;

        try
        {
            var jwk = new JsonWebKey(publicJwk);
            using var ecdsa = ECDsa.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint
                {
                    X = DecodeBase64Url(jwk.X),
                    Y = DecodeBase64Url(jwk.Y),
                },
            });

            return ecdsa.VerifyData(
                System.Text.Encoding.UTF8.GetBytes(ValourFederation.BuildInviteProofPayload(
                    grantId,
                    nodeDomain,
                    planetId,
                    recipientUserId,
                    maxUses,
                    passportId,
                    userId)),
                DecodeBase64Url(proof),
                HashAlgorithmName.SHA256);
        }
        catch
        {
            return false;
        }
    }

    private static bool HasPurpose(IDictionary<string, object> claims, string purpose) =>
        TryGetString(claims, "purpose", out var actual) && string.Equals(actual, purpose, StringComparison.Ordinal);

    private static bool HasCurrentProtocol(IDictionary<string, object> claims) =>
        TryGetInt(claims, "protocol", out var protocol) && protocol == ValourFederation.ProtocolVersion;

    private static bool TryGetString(IDictionary<string, object> claims, string name, out string value)
    {
        value = null;
        if (!claims.TryGetValue(name, out var raw) || raw is null)
            return false;
        value = raw switch
        {
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString(),
            _ => raw.ToString(),
        };
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetLong(IDictionary<string, object> claims, string name, out long value)
    {
        value = 0;
        return TryGetString(claims, name, out var raw) && long.TryParse(raw, out value);
    }

    private static bool TryGetInt(IDictionary<string, object> claims, string name, out int value)
    {
        value = 0;
        return TryGetString(claims, name, out var raw) && int.TryParse(raw, out value);
    }

    private static DateTime ReadTokenExpiry(string token)
    {
        try { return new JsonWebTokenHandler().ReadJsonWebToken(token).ValidTo; }
        catch { return DateTime.UtcNow; }
    }

    private static bool IsValidClientPublicJwk(string publicJwk)
    {
        try
        {
            var jwk = new JsonWebKey(publicJwk);
            return string.Equals(jwk.Kty, "EC", StringComparison.Ordinal) &&
                   string.Equals(jwk.Crv, "P-256", StringComparison.Ordinal) &&
                   !string.IsNullOrWhiteSpace(jwk.X) && !string.IsNullOrWhiteSpace(jwk.Y);
        }
        catch
        {
            return false;
        }
    }

    private static byte[] DecodeBase64Url(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
        return Convert.FromBase64String(base64);
    }
}
