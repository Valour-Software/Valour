using System.Net.Http.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Valour.Config.Configs;
using Valour.Database;
using Valour.Server.Api.Dynamic;
using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Server.Services;

/// <summary>
/// Orchestrates planet migration between the official network and community
/// nodes using signed snapshot export/import with full handoff:
///
///   1. Owner initiates on the SOURCE (hub) → source locks the planet and
///      issues a hub-signed migration grant bound to the destination.
///   2. Destination presents the grant to pull the signed snapshot, imports
///      it locally, adopts the planet stub, and confirms completion.
///   3. Source hands off: deletes the planet's data, keeping only the stub.
/// </summary>
public class FederationMigrationService
{
    private const string GrantPurpose = "valour-migration";
    private static readonly TimeSpan GrantLifetime = TimeSpan.FromMinutes(30);

    private readonly ValourDb _db;
    private readonly FederationKeyService _keyService;
    private readonly FederationHubService _hubService;
    private readonly FederationNodeService _nodeService;
    private readonly FederationNodeClient _nodeClient;
    private readonly PlanetSnapshotService _snapshotService;
    private readonly HostedPlanetService _hostedPlanetService;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<FederationMigrationService> _logger;

    public FederationMigrationService(
        ValourDb db,
        FederationKeyService keyService,
        FederationHubService hubService,
        FederationNodeService nodeService,
        FederationNodeClient nodeClient,
        PlanetSnapshotService snapshotService,
        HostedPlanetService hostedPlanetService,
        IHttpClientFactory httpFactory,
        ILogger<FederationMigrationService> logger)
    {
        _db = db;
        _keyService = keyService;
        _hubService = hubService;
        _nodeService = nodeService;
        _nodeClient = nodeClient;
        _snapshotService = snapshotService;
        _hostedPlanetService = hostedPlanetService;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    // ===================== Source (hub) side =====================

    /// <summary>
    /// Owner authorizes migrating their planet to a target node. Locks the
    /// planet and returns a signed grant the destination uses to pull.
    /// </summary>
    public async Task<TaskResult<MigrationInitiateResponse>> InitiateAsync(long ownerId, long planetId, string targetDomain)
    {
        if (!FederationHubService.HubEnabled)
            return TaskResult<MigrationInitiateResponse>.FromFailure("This instance is not a federation hub.");

        targetDomain = FederationHubService.NormalizeDomain(targetDomain);
        if (targetDomain is null)
            return TaskResult<MigrationInitiateResponse>.FromFailure("Invalid target domain.");

        var planet = await _db.Planets.FindAsync(planetId);
        if (planet is null)
            return TaskResult<MigrationInitiateResponse>.FromFailure("Planet not found.");

        if (planet.OwnerId != ownerId)
            return TaskResult<MigrationInitiateResponse>.FromFailure("Only the planet owner can migrate it.");

        var node = await _db.FederatedNodes.AsNoTracking().FirstOrDefaultAsync(x => x.Domain == targetDomain);
        if (node is null || node.Status != FederatedNodeStatus.Active)
            return TaskResult<MigrationInitiateResponse>.FromFailure("Target is not an active community node.");

        var existing = await _db.FederatedMigrations.FindAsync(planetId);
        if (existing is not null &&
            existing.Status == FederatedMigrationStatus.Pending &&
            existing.TargetDomain != targetDomain)
        {
            return TaskResult<MigrationInitiateResponse>.FromFailure("This planet is already migrating to a different target.");
        }

        // A pending migration to the same target is re-issued (retry-safe) — the
        // grant may have expired or an earlier attempt failed partway.
        if (existing is not null)
            _db.FederatedMigrations.Remove(existing);

        await _db.FederatedMigrations.AddAsync(new FederatedMigration
        {
            PlanetId = planetId,
            TargetDomain = targetDomain,
            Status = FederatedMigrationStatus.Pending,
            CreatedAt = DateTime.UtcNow,
        });

        // Lock the planet read-only for the migration window so no writes are
        // lost between the snapshot and the handoff. Evict the hosted cache so
        // the flag is live for the write path immediately.
        planet.LockedForMigration = true;
        await _db.SaveChangesAsync();
        _hostedPlanetService.Remove(planetId);

        var grant = await MintGrantAsync(planetId, targetDomain);
        if (grant is null)
            return TaskResult<MigrationInitiateResponse>.FromFailure("Could not sign the migration grant.");

        return TaskResult<MigrationInitiateResponse>.FromData(new MigrationInitiateResponse
        {
            PlanetId = planetId,
            TargetDomain = targetDomain,
            SourceDomain = HostingConfig.Current.RootDomain,
            Grant = grant,
            ExpiresAt = DateTime.UtcNow.Add(GrantLifetime),
        });
    }

    /// <summary>
    /// Owner cancels a pending migration — clears the lock so the planet is
    /// writable again. Prevents an abandoned migration from freezing a planet.
    /// </summary>
    public async Task<TaskResult> AbortAsync(long ownerId, long planetId)
    {
        var planet = await _db.Planets.FindAsync(planetId);
        if (planet is null)
            return TaskResult.FromFailure("Planet not found.");

        if (planet.OwnerId != ownerId)
            return TaskResult.FromFailure("Only the planet owner can abort a migration.");

        var migration = await _db.FederatedMigrations.FindAsync(planetId);
        if (migration is null || migration.Status != FederatedMigrationStatus.Pending)
            return TaskResult.FromFailure("No pending migration for this planet.");

        migration.Status = FederatedMigrationStatus.Aborted;
        migration.CompletedAt = DateTime.UtcNow;
        planet.LockedForMigration = false;
        await _db.SaveChangesAsync();
        _hostedPlanetService.Remove(planetId);

        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// Serves the planet snapshot to a destination holding a valid grant.
    /// </summary>
    public async Task<TaskResult<PlanetSnapshot>> GetSnapshotForGrantAsync(string grant)
    {
        var (planetId, target) = await ValidateGrantAsync(grant);
        if (planetId is null)
            return TaskResult<PlanetSnapshot>.FromFailure("Invalid or expired migration grant.");

        var migration = await _db.FederatedMigrations.FindAsync(planetId.Value);
        if (migration is null || migration.Status != FederatedMigrationStatus.Pending || migration.TargetDomain != target)
            return TaskResult<PlanetSnapshot>.FromFailure("No active migration for this grant.");

        return await _snapshotService.ExportAsync(planetId.Value);
    }

    /// <summary>
    /// Destination confirms it imported the planet; source performs the full
    /// handoff — deletes the planet data and keeps only the stub. Node-authed.
    /// </summary>
    public async Task<TaskResult> CompleteAsync(string nodeDomain, long planetId)
    {
        var migration = await _db.FederatedMigrations.FindAsync(planetId);
        if (migration is null || migration.Status != FederatedMigrationStatus.Pending)
            return TaskResult.FromFailure("No active migration for this planet.");

        if (migration.TargetDomain != nodeDomain)
            return TaskResult.FromFailure("This migration targets a different node.");

        // Capture stub info before the planet is deleted.
        var planet = await _db.Planets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == planetId);
        var memberCount = await _db.PlanetMembers.CountAsync(x => x.PlanetId == planetId);

        // Full handoff: delete the planet's data from this (official) source.
        var deleteResult = await _snapshotService.DeletePlanetDataAsync(planetId);
        if (!deleteResult.Success)
            return deleteResult;

        // Replace it with a stub pointing at the node — the hub now only knows
        // this planet lives on the node (discovery, invites, moderation).
        var existingStub = await _db.FederatedPlanetStubs.FindAsync(planetId);
        if (existingStub is not null)
            _db.FederatedPlanetStubs.Remove(existingStub);

        await _db.FederatedPlanetStubs.AddAsync(new FederatedPlanetStub
        {
            Id = planetId,
            NodeDomain = nodeDomain,
            Name = planet?.Name,
            Description = planet?.Description,
            OwnerId = planet?.OwnerId ?? 0,
            MemberCount = memberCount,
            Nsfw = planet?.Nsfw ?? false,
            Discoverable = planet?.Discoverable ?? false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });

        migration.Status = FederatedMigrationStatus.Completed;
        migration.CompletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        // Evict the now-deleted planet from the hosted-planet cache so this
        // node stops serving its stale in-memory copy.
        // (Multi-node clusters should also clear the Redis planet:{id} pin — TODO.)
        _hostedPlanetService.Remove(planetId);

        _logger.LogInformation("Planet {PlanetId} handed off to {Domain}", planetId, nodeDomain);
        return TaskResult.SuccessResult;
    }

    // ===================== Destination (node) side =====================

    /// <summary>
    /// Node-side: confirm the grant is for us, pull the snapshot from the
    /// source, import it locally, and confirm completion (the source then
    /// deletes its copy and creates the stub pointing here).
    /// </summary>
    public async Task<TaskResult> ImportFromGrantAsync(string grant, string sourceUrl)
    {
        if (!FederationNodeService.NodeEnabled)
            return TaskResult.FromFailure("This instance is not a community node.");

        // The node only reads the grant's claims for routing — it doesn't hold
        // the hub's keys. The SOURCE cryptographically validates the grant when
        // serving the snapshot and completing, so a bad grant is rejected there.
        var (parsedId, target) = ParseGrantClaims(grant);
        if (parsedId is null)
            return TaskResult.FromFailure("Malformed migration grant.");

        var planetId = parsedId.Value;

        if (target != FederationConfig.Current.NodeDomain)
            return TaskResult.FromFailure("This grant is for a different destination.");

        // Pull the snapshot from the source using the grant.
        PlanetSnapshot snapshot;
        try
        {
            var client = _httpFactory.CreateClient("federation");
            var url = sourceUrl.TrimEnd('/') + $"/api/federation/migrations/{planetId}/snapshot";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("X-Valour-Migration-Grant", grant);
            using var resp = await client.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
                return TaskResult.FromFailure($"Snapshot pull failed: {(int)resp.StatusCode}");
            snapshot = await resp.Content.ReadFromJsonAsync<PlanetSnapshot>();
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Snapshot pull threw for planet {PlanetId}", planetId);
            return TaskResult.FromFailure($"Snapshot pull failed: {e.Message}");
        }

        if (snapshot?.Planet is null || snapshot.Planet.Id != planetId)
            return TaskResult.FromFailure("Snapshot did not match the grant.");

        // Import the planet locally at its original ids.
        var import = await _snapshotService.ImportAsync(snapshot);
        if (!import.Success)
            return import;

        // Confirm to the source, which atomically deletes its copy and replaces
        // it with a stub pointing here (the hub owns both sides of the handoff).
        var complete = await ConfirmCompleteAsync(sourceUrl, planetId);
        if (!complete.Success)
            return TaskResult.FromFailure($"Imported locally, but source handoff failed: {complete.Message}");

        return TaskResult.SuccessResult;
    }

    private async Task<TaskResult> ConfirmCompleteAsync(string sourceUrl, long planetId)
    {
        var token = await _nodeService.MintS2STokenAsync(_keyService);
        if (token is null)
            return TaskResult.FromFailure("Node signing key unavailable.");

        try
        {
            var client = _httpFactory.CreateClient("federation");
            var url = sourceUrl.TrimEnd('/') + $"/api/federation/migrations/{planetId}/complete";
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Add(FederationApi.NodeAuthHeader, token);
            using var resp = await client.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
                return TaskResult.FromFailure($"Complete returned {(int)resp.StatusCode}");
            return TaskResult.SuccessResult;
        }
        catch (Exception e)
        {
            return TaskResult.FromFailure(e.Message);
        }
    }

    // ===================== Reverse: node → official (pull-back) =====================

    private const string PullbackPurpose = "valour-migration-pullback";

    /// <summary>
    /// Hub-driven pull-back of a community-hosted planet back to official. The
    /// owner authorizes on the hub (ground truth for accounts); the hub mints a
    /// hub-signed grant the node honors (the node already trusts hub signatures
    /// via JWKS), pulls the snapshot, imports the planet, removes the stub, and
    /// instructs the node to purge its copy. Full handoff, reversed.
    /// </summary>
    public async Task<TaskResult> PullBackAsync(long ownerId, long planetId)
    {
        if (!FederationHubService.HubEnabled)
            return TaskResult.FromFailure("This instance is not a federation hub.");

        var stub = await _db.FederatedPlanetStubs.FindAsync(planetId);
        if (stub is null)
            return TaskResult.FromFailure("No community-hosted planet with this id.");

        if (stub.OwnerId != ownerId)
            return TaskResult.FromFailure("Only the planet owner can pull it back.");

        if (await _db.Planets.IgnoreQueryFilters().AnyAsync(x => x.Id == planetId))
            return TaskResult.FromFailure("A planet with this id already exists on official.");

        var nodeDomain = stub.NodeDomain;
        var grant = await MintGrantAsync(planetId, nodeDomain, PullbackPurpose);
        if (grant is null)
            return TaskResult.FromFailure("Could not sign the pull-back grant.");

        var scheme = FederationConfig.Current?.AllowInsecure == true ? "http" : "https";
        var nodeBase = $"{scheme}://{nodeDomain}";

        // Pull the snapshot from the node.
        PlanetSnapshot snapshot;
        try
        {
            var client = _httpFactory.CreateClient("federation");
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{nodeBase}/api/federation/migrations/{planetId}/export");
            req.Headers.Add("X-Valour-Migration-Grant", grant);
            using var resp = await client.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
                return TaskResult.FromFailure($"Snapshot pull from node failed: {(int)resp.StatusCode}");
            snapshot = await resp.Content.ReadFromJsonAsync<PlanetSnapshot>();
        }
        catch (Exception e)
        {
            return TaskResult.FromFailure($"Snapshot pull from node failed: {e.Message}");
        }

        if (snapshot?.Planet is null || snapshot.Planet.Id != planetId)
            return TaskResult.FromFailure("Node snapshot did not match.");

        // Import as an official planet, remove the stub.
        var import = await _snapshotService.ImportAsync(snapshot);
        if (!import.Success)
            return import;

        _db.FederatedPlanetStubs.Remove(stub);
        await _db.SaveChangesAsync();

        // Instruct the node to purge its now-migrated copy.
        try
        {
            var client = _httpFactory.CreateClient("federation");
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{nodeBase}/api/federation/migrations/{planetId}/purge");
            req.Headers.Add("X-Valour-Migration-Grant", grant);
            using var resp = await client.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
                _logger.LogWarning("Node purge after pull-back returned {Status} for {PlanetId}", (int)resp.StatusCode, planetId);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Node purge after pull-back threw for {PlanetId}", planetId);
        }

        _logger.LogInformation("Planet {PlanetId} pulled back to official from {Domain}", planetId, nodeDomain);
        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// Node-side: serve a snapshot to the hub during a pull-back. Authorized by
    /// a hub-signed pull-back grant (validated against hub JWKS).
    /// </summary>
    public async Task<TaskResult<PlanetSnapshot>> ExportForPullBackAsync(string grant)
    {
        var planetId = await ValidatePullBackGrantAsync(grant);
        if (planetId is null)
            return TaskResult<PlanetSnapshot>.FromFailure("Invalid pull-back grant.");

        return await _snapshotService.ExportAsync(planetId.Value);
    }

    /// <summary>
    /// Node-side: delete the local planet after the hub confirms it imported it.
    /// </summary>
    public async Task<TaskResult> PurgeForPullBackAsync(string grant)
    {
        var planetId = await ValidatePullBackGrantAsync(grant);
        if (planetId is null)
            return TaskResult.FromFailure("Invalid pull-back grant.");

        var result = await _snapshotService.DeletePlanetDataAsync(planetId.Value);
        if (result.Success)
        {
            var stub = await _db.FederatedPlanetStubs.FindAsync(planetId.Value);
            if (stub is not null)
            {
                _db.FederatedPlanetStubs.Remove(stub);
                await _db.SaveChangesAsync();
            }
            _hostedPlanetService.Remove(planetId.Value);
        }
        return result;
    }

    /// <summary>
    /// Validates a hub-signed pull-back grant on the node: hub signature, our
    /// domain as audience, the pull-back purpose. Returns the planet id.
    /// </summary>
    private async Task<long?> ValidatePullBackGrantAsync(string grant)
    {
        var claims = await _nodeService.ValidateHubSignedTokenAsync(grant);
        if (claims is null)
            return null;

        if (!claims.TryGetValue("purpose", out var purpose) || purpose?.ToString() != PullbackPurpose)
            return null;

        var aud = claims.TryGetValue("aud", out var a) ? a?.ToString() : null;
        if (aud != FederationConfig.Current.NodeDomain)
            return null;

        if (!claims.TryGetValue("planet", out var planetRaw) || !long.TryParse(planetRaw?.ToString(), out var planetId))
            return null;

        return planetId;
    }

    // ===================== Grant crypto =====================

    private async Task<string> MintGrantAsync(long planetId, string targetDomain, string purpose = GrantPurpose)
    {
        var credentials = await _keyService.GetHubSigningCredentialsAsync();
        if (credentials is null)
            return null;

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = HostingConfig.Current.RootDomain,
            Audience = targetDomain,
            Expires = DateTime.UtcNow.Add(GrantLifetime),
            IssuedAt = DateTime.UtcNow,
            SigningCredentials = credentials,
            Claims = new Dictionary<string, object>
            {
                ["purpose"] = purpose,
                ["planet"] = planetId.ToString(),
            },
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    /// <summary>
    /// Reads a grant's claims WITHOUT signature validation — for the node's
    /// routing only. Never a security decision (the source validates).
    /// </summary>
    private static (long? planetId, string target) ParseGrantClaims(string grant)
    {
        if (string.IsNullOrWhiteSpace(grant))
            return (null, null);
        try
        {
            var jwt = new JsonWebTokenHandler().ReadJsonWebToken(grant);
            var planet = jwt.GetPayloadValue<string>("planet");
            var aud = jwt.Audiences.FirstOrDefault();
            return long.TryParse(planet, out var id) ? (id, aud) : (null, null);
        }
        catch
        {
            return (null, null);
        }
    }

    /// <summary>
    /// Validates a migration grant against the hub's own signing keys (the hub
    /// minted it). Returns (planetId, targetDomain) when valid.
    /// </summary>
    private async Task<(long? planetId, string target)> ValidateGrantAsync(string grant)
    {
        if (string.IsNullOrWhiteSpace(grant))
            return (null, null);

        var jwks = await _keyService.GetJwksJsonAsync();
        JsonWebKeySet keySet;
        try
        {
            keySet = new JsonWebKeySet(jwks);
        }
        catch
        {
            return (null, null);
        }

        var validation = new TokenValidationParameters
        {
            ValidIssuer = HostingConfig.Current.RootDomain,
            ValidateAudience = false, // audience is the target; checked by callers
            IssuerSigningKeys = keySet.GetSigningKeys(),
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            ValidAlgorithms = new[] { SecurityAlgorithms.EcdsaSha256 },
            RequireSignedTokens = true,
        };

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(grant, validation);
        if (!result.IsValid)
            return (null, null);

        if (!result.Claims.TryGetValue("purpose", out var purpose) || purpose?.ToString() != GrantPurpose)
            return (null, null);

        if (!result.Claims.TryGetValue("planet", out var planetRaw) || !long.TryParse(planetRaw?.ToString(), out var planetId))
            return (null, null);

        var target = (result.Claims.TryGetValue("aud", out var aud) ? aud?.ToString() : null);
        return (planetId, target);
    }
}
