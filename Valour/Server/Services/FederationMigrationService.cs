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
    /// Serves the planet snapshot to a destination holding a valid grant, and
    /// records the snapshot hash + timestamp so completion can require proof the
    /// target actually pulled and imported it.
    /// </summary>
    public async Task<TaskResult<PlanetSnapshot>> GetSnapshotForGrantAsync(string grant)
    {
        var (planetId, target) = await ValidateGrantAsync(grant);
        if (planetId is null)
            return TaskResult<PlanetSnapshot>.FromFailure("Invalid or expired migration grant.");

        var migration = await _db.FederatedMigrations.FindAsync(planetId.Value);
        if (migration is null || migration.Status != FederatedMigrationStatus.Pending || migration.TargetDomain != target)
            return TaskResult<PlanetSnapshot>.FromFailure("No active migration for this grant.");

        var export = await _snapshotService.ExportAsync(planetId.Value);
        if (!export.Success || export.Data is null)
            return TaskResult<PlanetSnapshot>.FromFailure(export.Message ?? "Failed to export snapshot.");

        // Hash a canonical serialization of the snapshot object (not the wire
        // bytes, which a compressing proxy could alter). The target re-derives the
        // same hash from its imported copy — a mismatch fails safe: the source
        // keeps its data.
        migration.SnapshotHash = SnapshotHash(export.Data);
        migration.SnapshotServedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return TaskResult<PlanetSnapshot>.FromData(export.Data);
    }

    /// <summary>
    /// Canonical hash of a snapshot: SHA-256 over its default (compact, declared-
    /// order) System.Text.Json serialization. Both source and target compute this
    /// over the same logical object, so it is independent of wire encoding.
    /// </summary>
    private static string SnapshotHash(PlanetSnapshot snapshot)
    {
        var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(snapshot);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
    }

    /// <summary>
    /// Destination confirms it imported the planet; source performs the full
    /// handoff — deletes the planet data and keeps only the stub. Node-authed.
    /// </summary>
    public async Task<TaskResult> CompleteAsync(string nodeDomain, long planetId, string grant, string importedHash)
    {
        var migration = await _db.FederatedMigrations.FindAsync(planetId);
        if (migration is null || migration.Status != FederatedMigrationStatus.Pending)
            return TaskResult.FromFailure("No active migration for this planet.");

        if (migration.TargetDomain != nodeDomain)
            return TaskResult.FromFailure("This migration targets a different node.");

        // The node's S2S token proves who it is; it must ALSO present the hub-signed
        // grant for THIS migration — completion is a destructive act, not something a
        // node can trigger from identity alone.
        var (grantPlanetId, grantTarget) = await ValidateGrantAsync(grant);
        if (grantPlanetId != planetId || grantTarget != nodeDomain)
            return TaskResult.FromFailure("A valid migration grant for this planet and node is required.");

        // Never delete source data the target didn't actually receive. The snapshot
        // must have been served, and the target must echo its exact hash — proof it
        // pulled and imported the real data before we hand off.
        if (migration.SnapshotServedAt is null || string.IsNullOrEmpty(migration.SnapshotHash))
            return TaskResult.FromFailure("The snapshot has not been pulled yet; cannot complete.");

        if (!string.Equals(migration.SnapshotHash, importedHash?.Trim().ToLowerInvariant(), StringComparison.Ordinal))
            return TaskResult.FromFailure("Imported snapshot hash does not match the served snapshot.");

        // Capture stub info before the planet is deleted.
        var planet = await _db.Planets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == planetId);
        var memberCount = await _db.PlanetMembers.CountAsync(x => x.PlanetId == planetId);

        // Record the identities legitimately present at handoff (members + message
        // authors + owner). A later pull-back trusts only these as official
        // identities; anything the returning node adds beyond them is fabricated.
        var trustedIds = new HashSet<long>();
        trustedIds.UnionWith(await _db.PlanetMembers.Where(x => x.PlanetId == planetId).Select(x => x.UserId).ToListAsync());
        trustedIds.UnionWith(await _db.Messages.Where(x => x.PlanetId == planetId).Select(x => x.AuthorUserId).Distinct().ToListAsync());
        if (planet is not null)
            trustedIds.Add(planet.OwnerId);

        // Create the stub and mark the migration completed FIRST, in one atomic
        // save, BEFORE deleting the source. Ordering matters for crash safety:
        // if the delete then fails, the planet still exists on official AND has a
        // stub — a recoverable duplicate (retry the delete) — rather than the
        // unrecoverable "source deleted, no stub". The planet stays
        // LockedForMigration throughout, so it's read-only in that window.
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
            TrustedUserIdsJson = System.Text.Json.JsonSerializer.Serialize(trustedIds),
        });

        migration.Status = FederatedMigrationStatus.Completed;
        migration.CompletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        // Now delete the source data. A failure here leaves a recoverable
        // duplicate (stub + still-present, locked source) rather than data loss.
        var deleteResult = await _snapshotService.DeletePlanetDataAsync(planetId);
        if (!deleteResult.Success)
        {
            _logger.LogError(
                "Handoff of planet {PlanetId} to {Domain}: stub created but source delete failed ({Message}). Source is retained and still locked; retry the delete.",
                planetId, nodeDomain, deleteResult.Message);
            return TaskResult.FromFailure("Handoff recorded, but source cleanup failed and will be retried.");
        }

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

        // Ignore the caller-supplied source URL: forward migrations always come
        // from the trusted hub (the grant issuer). Trusting it would let a caller
        // point the node at an attacker's server and capture the node's S2S token
        // when it confirms completion there.
        var source = FederationConfig.Current.HubUrl;

        // Pull the snapshot from the source using the grant.
        PlanetSnapshot snapshot;
        try
        {
            var client = _httpFactory.CreateClient("federation");
            var url = source.TrimEnd('/') + $"/api/federation/migrations/{planetId}/snapshot";
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

        // Prove to the source we imported exactly what it served: the hash of our
        // copy, computed the same canonical way, must match.
        var importedHash = SnapshotHash(snapshot);

        // Import the planet locally at its original ids.
        var import = await _snapshotService.ImportAsync(snapshot);
        if (!import.Success)
            return import;

        // Confirm to the source, which atomically deletes its copy and replaces
        // it with a stub pointing here (the hub owns both sides of the handoff).
        var complete = await ConfirmCompleteAsync(source, planetId, grant, importedHash);
        if (!complete.Success)
            return TaskResult.FromFailure($"Imported locally, but source handoff failed: {complete.Message}");

        return TaskResult.SuccessResult;
    }

    private async Task<TaskResult> ConfirmCompleteAsync(string sourceUrl, long planetId, string grant, string importedHash)
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
            req.Headers.Add("X-Valour-Migration-Grant", grant);
            req.Headers.Add("X-Valour-Snapshot-Hash", importedHash);
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
            {
                await TryAbortPullBackOnNodeAsync(nodeBase, planetId, grant);
                return TaskResult.FromFailure($"Snapshot pull from node failed: {(int)resp.StatusCode}");
            }
            snapshot = await resp.Content.ReadFromJsonAsync<PlanetSnapshot>();
        }
        catch (Exception e)
        {
            await TryAbortPullBackOnNodeAsync(nodeBase, planetId, grant);
            return TaskResult.FromFailure($"Snapshot pull from node failed: {e.Message}");
        }

        if (snapshot?.Planet is null || snapshot.Planet.Id != planetId)
        {
            await TryAbortPullBackOnNodeAsync(nodeBase, planetId, grant);
            return TaskResult.FromFailure("Node snapshot did not match.");
        }

        // The node controls this snapshot. Sanitize node-asserted identity before
        // importing it as official data.
        await SanitizePulledBackSnapshotAsync(snapshot, planetId, ownerId, stub.TrustedUserIdsJson);

        // Import as an official planet, remove the stub.
        var import = await _snapshotService.ImportAsync(snapshot);
        if (!import.Success)
        {
            // Import failed after the node froze itself for export — unfreeze it.
            await TryAbortPullBackOnNodeAsync(nodeBase, planetId, grant);
            return import;
        }

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
    /// Best-effort: tell the node to unfreeze a planet whose pull-back the hub
    /// could not finish, so a failed import doesn't leave it read-only.
    /// </summary>
    private async Task TryAbortPullBackOnNodeAsync(string nodeBase, long planetId, string grant)
    {
        try
        {
            var client = _httpFactory.CreateClient("federation");
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{nodeBase}/api/federation/migrations/{planetId}/pullback-abort");
            req.Headers.Add("X-Valour-Migration-Grant", grant);
            using var resp = await client.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
                _logger.LogWarning("Pull-back abort on node returned {Status} for {PlanetId}", (int)resp.StatusCode, planetId);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Pull-back abort on node threw for {PlanetId}", planetId);
        }
    }

    /// <summary>
    /// A pull-back snapshot is authored by an untrusted community node, but it's
    /// imported as OFFICIAL data. This strips node-fabricated identity: the owner
    /// is pinned to the authenticated initiator, and any membership or message
    /// that claims an EXISTING OFFICIAL account is dropped unless that account is
    /// trusted — it was present at migration-out (recorded then), consented via a
    /// federated join, or is the initiator. Federated/unknown ids pass through as
    /// federated shadows (they can't impersonate a real account).
    /// </summary>
    private async Task SanitizePulledBackSnapshotAsync(
        PlanetSnapshot snapshot, long planetId, long ownerId, string trustedJson)
    {
        // Owner is the person who authorized the pull-back on the hub, never the
        // node's claim.
        snapshot.Planet.OwnerId = ownerId;

        var trusted = new HashSet<long> { ownerId };
        if (!string.IsNullOrWhiteSpace(trustedJson))
        {
            try { trusted.UnionWith(System.Text.Json.JsonSerializer.Deserialize<List<long>>(trustedJson) ?? new()); }
            catch { /* corrupt record → trust only the initiator */ }
        }
        trusted.UnionWith(await _db.FederatedMemberships
            .Where(x => x.PlanetId == planetId).Select(x => x.UserId).ToListAsync());

        // Which referenced ids are real official (non-federated) accounts here?
        var referenced = new HashSet<long>();
        referenced.UnionWith(snapshot.Members.Select(m => m.UserId));
        referenced.UnionWith(snapshot.Messages.Select(m => m.AuthorUserId));
        var officialIds = (await _db.Users
            .Where(u => referenced.Contains(u.Id) && !u.IsFederated)
            .Select(u => u.Id).ToListAsync()).ToHashSet();

        // An official id may only be asserted if trusted; otherwise it's forged.
        bool Forged(long userId) => officialIds.Contains(userId) && !trusted.Contains(userId);

        var droppedMembers = snapshot.Members.RemoveAll(m => Forged(m.UserId));

        var forgedMessageIds = snapshot.Messages.Where(m => Forged(m.AuthorUserId)).Select(m => m.Id).ToHashSet();
        if (forgedMessageIds.Count > 0)
        {
            snapshot.Messages.RemoveAll(m => forgedMessageIds.Contains(m.Id));
            snapshot.Attachments.RemoveAll(a => forgedMessageIds.Contains(a.MessageId));
            snapshot.Reactions.RemoveAll(r => forgedMessageIds.Contains(r.MessageId));
            snapshot.Mentions.RemoveAll(x => forgedMessageIds.Contains(x.MessageId));
        }

        // Bans that name an untrusted official issuer/target are also fabricated.
        var droppedBans = snapshot.Bans.RemoveAll(b => Forged(b.IssuerId) || Forged(b.TargetId));

        if (droppedMembers > 0 || forgedMessageIds.Count > 0 || droppedBans > 0)
        {
            _logger.LogWarning(
                "Pull-back of planet {PlanetId} from {Node}: dropped {Members} forged memberships, {Messages} forged messages, {Bans} forged bans",
                planetId, snapshot.Planet.Id, droppedMembers, forgedMessageIds.Count, droppedBans);
        }
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

        // Freeze the local planet read-only for the hub's import window so no
        // writes are lost between this export and the purge. Reversible via
        // AbortPullBackAsync if the hub's import fails.
        var planet = await _db.Planets.FindAsync(planetId.Value);
        if (planet is not null && !planet.LockedForMigration)
        {
            planet.LockedForMigration = true;
            await _db.SaveChangesAsync();
            _hostedPlanetService.Remove(planetId.Value);
        }

        return await _snapshotService.ExportAsync(planetId.Value);
    }

    /// <summary>
    /// Node-side: delete the local planet after the hub confirms it imported it.
    /// Only a planet that was frozen for export can be purged, so a bare grant
    /// replay can't destroy a live planet that never entered the pull-back flow.
    /// </summary>
    public async Task<TaskResult> PurgeForPullBackAsync(string grant)
    {
        var planetId = await ValidatePullBackGrantAsync(grant);
        if (planetId is null)
            return TaskResult.FromFailure("Invalid pull-back grant.");

        var planet = await _db.Planets.FindAsync(planetId.Value);
        if (planet is not null && !planet.LockedForMigration)
            return TaskResult.FromFailure("This planet is not in a pull-back; refusing to purge.");

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
    /// Node-side: unfreeze a planet whose pull-back the hub could not complete,
    /// so a failed import doesn't leave it read-only forever.
    /// </summary>
    public async Task<TaskResult> AbortPullBackAsync(string grant)
    {
        var planetId = await ValidatePullBackGrantAsync(grant);
        if (planetId is null)
            return TaskResult.FromFailure("Invalid pull-back grant.");

        var planet = await _db.Planets.FindAsync(planetId.Value);
        if (planet is not null && planet.LockedForMigration)
        {
            planet.LockedForMigration = false;
            await _db.SaveChangesAsync();
            _hostedPlanetService.Remove(planetId.Value);
        }

        return TaskResult.SuccessResult;
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
