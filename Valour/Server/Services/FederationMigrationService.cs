using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Valour.Config.Configs;
using Valour.Database;
using Valour.Server.Api.Dynamic;
using Valour.Server.Cdn;
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
///   3. Source marks the handoff ready and keeps a locked recovery copy. The
///      owner explicitly finalizes source deletion only after verifying the
///      destination, because a remote server cannot cryptographically prove it
///      committed imported data.
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

        var existing = await _db.FederatedMigrations.FindAsync(planetId);
        if (existing is not null &&
            existing.Status == FederatedMigrationStatus.Pending &&
            existing.TargetDomain != targetDomain)
        {
            return TaskResult<MigrationInitiateResponse>.FromFailure("This planet is already migrating to a different target.");
        }
        if (existing is not null &&
            existing.Status == FederatedMigrationStatus.Completed &&
            existing.TargetDomain != targetDomain)
        {
            // The destination now owns a live copy. Reusing the retained
            // source snapshot for a different node would create a second
            // writable destination; recovery grants may only target the
            // already-completed destination so it can retry confirmation.
            return TaskResult<MigrationInitiateResponse>.FromFailure(
                "This migration already completed to a different node. Finalize it after verification, then use pull-back if needed.");
        }

        var node = await _db.FederatedNodes.AsNoTracking().FirstOrDefaultAsync(x => x.Domain == targetDomain);
        if (node is null || node.Status != FederatedNodeStatus.Active)
            return TaskResult<MigrationInitiateResponse>.FromFailure("Target is not an active community node.");

        // A retry rotates the signed capability. Completed handoffs retain their
        // recovery copy, so they can also issue a fresh grant for an idempotent
        // destination confirmation without reopening the handoff.
        var grantId = Guid.NewGuid().ToString("N");
        var isCompletedRecovery = existing?.Status == FederatedMigrationStatus.Completed &&
                                  existing.TargetDomain == targetDomain;
        if (existing is null || !isCompletedRecovery && existing.Status != FederatedMigrationStatus.Pending)
        {
            if (existing is not null)
                _db.FederatedMigrations.Remove(existing);

            existing = new FederatedMigration
            {
                PlanetId = planetId,
                TargetDomain = targetDomain,
                Status = FederatedMigrationStatus.Pending,
                CreatedAt = DateTime.UtcNow,
            };
            await _db.FederatedMigrations.AddAsync(existing);
        }

        existing.GrantId = grantId;
        existing.CreatedAt = DateTime.UtcNow;

        // The owner explicitly selected this community server while starting
        // the migration. Record that consent now so the owner can establish a
        // normal federated session after the handoff as well as before it.
        if (!await _db.FederatedAcceptedDomains.AnyAsync(x => x.UserId == ownerId && x.Domain == targetDomain))
        {
            await _db.FederatedAcceptedDomains.AddAsync(new FederatedAcceptedDomain
            {
                UserId = ownerId,
                Domain = targetDomain,
                AcceptedAt = DateTime.UtcNow,
            });
        }

        // Lock the planet read-only for the migration window so no writes are
        // lost between the snapshot and the handoff. Evict the hosted cache so
        // the flag is live for the write path immediately.
        planet.LockedForMigration = true;
        await _db.SaveChangesAsync();
        _hostedPlanetService.Remove(planetId);

        var grant = await MintGrantAsync(planetId, targetDomain, ownerId, grantId);
        if (grant is null)
        {
            // Do not strand a planet in read-only migration state when signing
            // infrastructure is unavailable.
            if (!isCompletedRecovery)
            {
                var failedMigration = await _db.FederatedMigrations.FindAsync(planetId);
                if (failedMigration is not null)
                    _db.FederatedMigrations.Remove(failedMigration);
                planet.LockedForMigration = false;
                await _db.SaveChangesAsync();
            }
            _hostedPlanetService.Remove(planetId);
            return TaskResult<MigrationInitiateResponse>.FromFailure("Could not sign the migration grant.");
        }

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
    /// Owner cancels a handoff before the destination has confirmed it. Once a
    /// handoff is complete, restoring the source without deleting the
    /// destination would create two writable copies; use the verified pull-back
    /// protocol instead.
    /// </summary>
    public async Task<TaskResult> AbortAsync(long ownerId, long planetId)
    {
        if (!FederationHubService.HubEnabled)
            return TaskResult.FromFailure("This instance is not a federation hub.");

        var planet = await _db.Planets.FindAsync(planetId);
        if (planet is null)
            return TaskResult.FromFailure("Planet not found.");

        if (planet.OwnerId != ownerId)
            return TaskResult.FromFailure("Only the planet owner can abort a migration.");

        var migration = await _db.FederatedMigrations.FindAsync(planetId);
        if (migration?.Status == FederatedMigrationStatus.Completed)
            return TaskResult.FromFailure(
                "A completed migration cannot be aborted because it would create two writable copies. Finalize it after verification, then use pull-back if needed.");
        if (migration is null || migration.Status != FederatedMigrationStatus.Pending)
            return TaskResult.FromFailure("No migration that can be aborted for this planet.");

        migration.Status = FederatedMigrationStatus.Aborted;
        migration.CompletedAt = DateTime.UtcNow;
        planet.LockedForMigration = false;
        await _db.SaveChangesAsync();
        _hostedPlanetService.Remove(planetId);

        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// Lists active and completed handoffs for the current owner. This is
    /// intentionally metadata only: grants are short-lived capabilities and
    /// must never be returned by a later settings refresh.
    /// </summary>
    public async Task<TaskResult<List<MigrationStatusResponse>>> GetOwnerMigrationsAsync(long ownerId)
    {
        if (!FederationHubService.HubEnabled)
            return TaskResult<List<MigrationStatusResponse>>.FromFailure("This instance is not a federation hub.");

        var migrations = await (
                from migration in _db.FederatedMigrations.AsNoTracking()
                join planet in _db.Planets.IgnoreQueryFilters().AsNoTracking()
                    on migration.PlanetId equals planet.Id
                where planet.OwnerId == ownerId &&
                      (migration.Status == FederatedMigrationStatus.Pending ||
                       migration.Status == FederatedMigrationStatus.Completed)
                orderby migration.CreatedAt descending
                select new MigrationStatusResponse
                {
                    PlanetId = migration.PlanetId,
                    PlanetName = planet.Name,
                    TargetDomain = migration.TargetDomain,
                    Status = migration.Status.ToString(),
                    CreatedAt = migration.CreatedAt,
                    CompletedAt = migration.CompletedAt,
                }).ToListAsync();

        return TaskResult<List<MigrationStatusResponse>>.FromData(migrations);
    }

    /// <summary>
    /// Serves the planet snapshot to a destination holding a valid grant, and
    /// records the snapshot hash + timestamp so completion can require proof the
    /// target received the exact source representation.
    /// </summary>
    public async Task<TaskResult<PlanetSnapshot>> GetSnapshotForGrantAsync(
        string requestingNodeDomain,
        long requestedPlanetId,
        string grant)
    {
        if (!FederationHubService.HubEnabled)
            return TaskResult<PlanetSnapshot>.FromFailure("This instance is not a federation hub.");

        var (planetId, target, grantId, _, purpose) = await ValidateGrantAsync(grant);
        if (planetId is null || purpose != GrantPurpose)
            return TaskResult<PlanetSnapshot>.FromFailure("Invalid or expired migration grant.");

        if (planetId.Value != requestedPlanetId)
            return TaskResult<PlanetSnapshot>.FromFailure("Migration grant does not match the requested planet.");

        if (!string.Equals(target, requestingNodeDomain, StringComparison.Ordinal))
            return TaskResult<PlanetSnapshot>.FromFailure("Migration grant is not authorized for this node.");

        var migration = await _db.FederatedMigrations.FindAsync(planetId.Value);
        if (migration is null || migration.Status != FederatedMigrationStatus.Pending ||
            migration.TargetDomain != target || migration.GrantId != grantId)
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
    /// Destination confirms it received the exact snapshot; source records the
    /// handoff and keeps a locked recovery copy. Node-authed.
    /// </summary>
    public async Task<TaskResult> CompleteAsync(string nodeDomain, long planetId, string grant, string importedHash)
    {
        if (!FederationHubService.HubEnabled)
            return TaskResult.FromFailure("This instance is not a federation hub.");

        var migration = await _db.FederatedMigrations.FindAsync(planetId);
        if (migration is null)
            return TaskResult.FromFailure("No active migration for this planet.");

        if (migration.TargetDomain != nodeDomain)
            return TaskResult.FromFailure("This migration targets a different node.");

        // The node's S2S token proves who it is; it must ALSO present the hub-signed
        // grant for THIS migration — completion is a destructive act, not something a
        // node can trigger from identity alone.
        var (grantPlanetId, grantTarget, grantId, _, purpose) = await ValidateGrantAsync(grant);
        if (grantPlanetId != planetId || grantTarget != nodeDomain || purpose != GrantPurpose ||
            migration.GrantId != grantId)
            return TaskResult.FromFailure("A valid migration grant for this planet and node is required.");

        // The snapshot must have been served and echoed exactly. This proves the
        // target received the source representation, but deliberately does NOT
        // claim to prove a remote database committed it. Consequently this method
        // does not delete the official source; only the owner can finalize that
        // irreversible step after verifying their destination.
        if (migration.SnapshotServedAt is null || string.IsNullOrEmpty(migration.SnapshotHash))
            return TaskResult.FromFailure("The snapshot has not been pulled yet; cannot complete.");

        if (!string.Equals(migration.SnapshotHash, importedHash?.Trim().ToLowerInvariant(), StringComparison.Ordinal))
            return TaskResult.FromFailure("Imported snapshot hash does not match the served snapshot.");

        // The destination may have committed the import and then lost the HTTP
        // response. Make its safe replay a no-op rather than stranding both
        // copies behind a one-shot completion call.
        if (migration.Status == FederatedMigrationStatus.Completed)
            return TaskResult.SuccessResult;

        if (migration.Status != FederatedMigrationStatus.Pending)
            return TaskResult.FromFailure("This migration is no longer pending.");

        // Capture stub info before the planet is deleted.
        var planet = await _db.Planets.FirstOrDefaultAsync(x => x.Id == planetId);
        if (planet is null)
            return TaskResult.FromFailure("Source planet no longer exists.");
        var memberCount = await _db.PlanetMembers.CountAsync(x => x.PlanetId == planetId);

        // The owner is the only hub identity that remains authoritative when a
        // community-hosted planet returns. Community-node data is not signed by
        // individual hub users, so a historical membership or author id cannot
        // authenticate a later node-supplied record during pull-back.
        var sourceMemberIds = await _db.PlanetMembers
            .Where(x => x.PlanetId == planetId)
            .Select(x => x.UserId)
            .ToListAsync();

        // Create the stub and mark the migration complete in one save. Hide the
        // locked source from official discovery, but retain it as a recovery copy
        // until its owner explicitly finalizes deletion.
        var existingStub = await _db.FederatedPlanetStubs.FindAsync(planetId);
        if (existingStub is not null)
            _db.FederatedPlanetStubs.Remove(existingStub);

        // The imported member rows on the destination only become usable after
        // the hub includes these grants in its audience-scoped exchange token.
        // Carry every existing source member across before hiding the official
        // copy, so migration does not silently strand a community's members.
        var existingFederatedMembers = (await _db.FederatedMemberships
            .Where(x => x.PlanetId == planetId)
            .Select(x => x.UserId)
            .ToListAsync()).ToHashSet();
        foreach (var memberId in sourceMemberIds.Where(x => !existingFederatedMembers.Contains(x)))
        {
            await _db.FederatedMemberships.AddAsync(new FederatedMembership
            {
                UserId = memberId,
                PlanetId = planetId,
                NodeDomain = nodeDomain,
                JoinedAt = DateTime.UtcNow,
            });
        }

        await _db.FederatedPlanetStubs.AddAsync(new FederatedPlanetStub
        {
            Id = planetId,
            NodeDomain = nodeDomain,
            Name = planet?.Name,
            Description = planet?.Description,
            OwnerId = planet?.OwnerId ?? 0,
            MemberCount = memberCount,
            Nsfw = planet?.Nsfw ?? false,
            Public = planet?.Public ?? false,
            Discoverable = planet?.Discoverable ?? false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            // Kept for schema compatibility and for older hubs that read this
            // field. New pull-backs trust only the hub-verified owner.
            TrustedUserIdsJson = System.Text.Json.JsonSerializer.Serialize(new[] { planet.OwnerId }),
        });

        migration.Status = FederatedMigrationStatus.Completed;
        migration.CompletedAt = DateTime.UtcNow;
        migration.SourcePublic ??= planet.Public;
        migration.SourceDiscoverable ??= planet.Discoverable;
        planet.Public = false;
        planet.Discoverable = false;
        await _db.SaveChangesAsync();
        _hostedPlanetService.Remove(planetId);

        _logger.LogInformation(
            "Planet {PlanetId} handoff to {Domain} is ready; retained locked source until owner finalizes deletion",
            planetId, nodeDomain);
        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// Owner-only destructive finalization for a completed forward migration.
    /// A third-party node cannot prove that it committed data, so only the owner
    /// who authorized the move may release the locked official recovery copy.
    /// </summary>
    public async Task<TaskResult> FinalizeAsync(long ownerId, long planetId)
    {
        if (!FederationHubService.HubEnabled)
            return TaskResult.FromFailure("This instance is not a federation hub.");

        var migration = await _db.FederatedMigrations.FindAsync(planetId);
        if (migration is null || migration.Status != FederatedMigrationStatus.Completed)
            return TaskResult.FromFailure("This migration is not ready to finalize.");

        var planet = await _db.Planets.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == planetId);
        if (planet is null)
            return TaskResult.SuccessResult; // idempotent after a successful finalization

        if (planet.OwnerId != ownerId)
            return TaskResult.FromFailure("Only the planet owner can finalize a migration.");

        var stub = await _db.FederatedPlanetStubs.FindAsync(planetId);
        if (stub is null || !string.Equals(stub.NodeDomain, migration.TargetDomain, StringComparison.Ordinal))
            return TaskResult.FromFailure("Destination handoff record is missing or changed; source data was retained.");

        var deleteResult = await _snapshotService.DeletePlanetDataAsync(planetId);
        if (!deleteResult.Success)
            return TaskResult.FromFailure($"Source cleanup failed; the locked recovery copy was retained. {deleteResult.Message}");

        _hostedPlanetService.Remove(planetId);
        _logger.LogInformation("Planet {PlanetId} source copy finalized after owner confirmation", planetId);
        return TaskResult.SuccessResult;
    }

    // ===================== Destination (node) side =====================

    /// <summary>
    /// Node-side: confirm the grant is for us, pull the snapshot from the
    /// source, import it locally, and confirm completion (the source then
    /// deletes its copy and creates the stub pointing here).
    /// </summary>
    public async Task<TaskResult> ImportFromGrantAsync(long importingUserId, string grant, string sourceUrl)
    {
        if (!FederationNodeService.NodeEnabled)
            return TaskResult.FromFailure("This instance is not a community node.");

        // The destination validates the hub signature itself before it performs
        // any import work. This binds the browser request to the owner who
        // authorized the move instead of treating the grant as a transferable
        // snapshot capability.
        var claims = await _nodeService.ValidateHubSignedTokenAsync(
            grant, FederationConfig.Current.NodeDomain);
        if (!TryReadForwardGrant(claims, out var planetId, out var ownerId, out var grantId))
            return TaskResult.FromFailure("Invalid or expired migration grant.");
        if (ownerId != importingUserId)
            return TaskResult.FromFailure("Only the planet owner can import this migration.");

        // Ignore the caller-supplied source URL: forward migrations always come
        // from the trusted hub (the grant issuer). Trusting it would let a caller
        // point the node at an attacker's server and capture the node's S2S token
        // when it confirms completion there.
        var source = FederationConfig.Current.HubUrl;
        var sourceDomain = new Uri(source).Host;

        var receipt = await _db.FederatedImportReceipts.FindAsync(planetId);
        if (receipt is not null &&
            (!string.Equals(receipt.SourceDomain, sourceDomain, StringComparison.OrdinalIgnoreCase) ||
             receipt.OwnerId != ownerId))
        {
            return TaskResult.FromFailure("This planet id is reserved by a different migration.");
        }

        var localPlanetExists = await _db.Planets.AnyAsync(x => x.Id == planetId);
        if (receipt is not null &&
            !string.Equals(receipt.GrantId, grantId, StringComparison.Ordinal))
        {
            // A completed source may reissue a grant after the destination lost
            // its response. Let that idempotent confirmation win before
            // touching local data. It restores the already-imported copy
            // without fetching a completed source snapshot again.
            if (localPlanetExists)
            {
                var resumeCompleted = await ConfirmCompleteAsync(source, planetId, grant, receipt.SnapshotHash);
                if (resumeCompleted.Success)
                {
                    receipt.GrantId = grantId;
                    await RestoreImportedVisibilityAsync(planetId, receipt);
                    return TaskResult.SuccessResult;
                }
            }

            // A different jti after an unconfirmed import normally means the
            // owner aborted the source and started over. The old copy is safe
            // to discard only when it is still the hidden, read-only recovery
            // copy we created. This prevents a new grant from confirming stale
            // data while never deleting a live planet on ambiguous state.
            var stalePlanet = localPlanetExists
                ? await _db.Planets.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == planetId)
                : null;
            if (stalePlanet is not null &&
                (!stalePlanet.LockedForMigration || stalePlanet.Public || stalePlanet.Discoverable))
            {
                return TaskResult.FromFailure(
                    "This destination has a previous migration copy that is not safely recoverable. Resolve it before retrying.");
            }

            if (stalePlanet is not null)
            {
                var deleteStale = await _snapshotService.DeletePlanetDataAsync(planetId);
                if (!deleteStale.Success)
                    return TaskResult.FromFailure($"Could not reset the previous destination import: {deleteStale.Message}");
            }

            _db.FederatedImportReceipts.Remove(receipt);
            await _db.SaveChangesAsync();
            receipt = null;
            localPlanetExists = false;
        }

        if (receipt is not null && localPlanetExists)
        {
            // A previous request got far enough to import. Confirmation is
            // idempotent on the source, so this retry never creates a second
            // destination copy and also recovers from a lost HTTP response.
            await LockPendingImportedPlanetAsync(planetId);
            var resumeComplete = await ConfirmCompleteAsync(source, planetId, grant, receipt.SnapshotHash);
            if (!resumeComplete.Success)
                return TaskResult.FromFailure($"Imported locally, but source handoff is still pending: {resumeComplete.Message}");

            await RestoreImportedVisibilityAsync(planetId, receipt);
            return TaskResult.SuccessResult;
        }

        if (localPlanetExists)
            return TaskResult.FromFailure("A planet with this id already exists here.");

        // Pull the snapshot from the source using both the owner-bound grant
        // and this node's S2S identity. A leaked grant alone cannot export data.
        PlanetSnapshot snapshot;
        try
        {
            var nodeToken = await _nodeService.MintS2STokenAsync(_keyService);
            if (nodeToken is null)
                return TaskResult.FromFailure("Node signing key unavailable.");

            var client = _httpFactory.CreateClient("federation");
            var url = source.TrimEnd('/') + $"/api/federation/migrations/{planetId}/snapshot";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add(FederationApi.NodeAuthHeader, nodeToken);
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

        // Prove to the source we imported exactly what it served. Persist the
        // hash before import so a crash between import and confirmation can be
        // resumed safely by the next owner request.
        var importedHash = SnapshotHash(snapshot);
        if (receipt is not null && !string.Equals(receipt.SnapshotHash, importedHash, StringComparison.Ordinal))
            return TaskResult.FromFailure("The source snapshot changed after an earlier import attempt; abort and restart the migration.");

        var createdReceipt = receipt is null;
        receipt ??= new FederatedImportReceipt
        {
            PlanetId = planetId,
            SourceDomain = sourceDomain,
            OwnerId = ownerId,
            GrantId = grantId,
            SnapshotHash = importedHash,
            SourcePublic = snapshot.Planet.Public,
            SourceDiscoverable = snapshot.Planet.Discoverable,
            CreatedAt = DateTime.UtcNow,
        };

        if (createdReceipt)
        {
            await _db.FederatedImportReceipts.AddAsync(receipt);
            await _db.SaveChangesAsync();
        }

        // Keep the destination hidden until the source records the handoff. A
        // failed confirmation therefore cannot produce two discoverable copies.
        var pendingSnapshot = CloneForPendingImport(snapshot);
        var import = await _snapshotService.ImportAsync(pendingSnapshot);
        if (!import.Success)
        {
            if (createdReceipt)
            {
                _db.FederatedImportReceipts.Remove(receipt);
                await _db.SaveChangesAsync();
            }
            return import;
        }

        // The imported destination copy must not accept writes until its source
        // acknowledges the handoff. If confirmation fails and the owner aborts
        // the source, this remains a hidden, read-only recovery copy instead
        // of becoming a second writable planet.
        await LockPendingImportedPlanetAsync(planetId);

        // Confirm the handoff to the source. It replaces discovery with a stub
        // but retains its locked recovery copy until the owner finalizes.
        var complete = await ConfirmCompleteAsync(source, planetId, grant, importedHash);
        if (!complete.Success)
            return TaskResult.FromFailure($"Imported locally, but source handoff failed: {complete.Message}");

        await RestoreImportedVisibilityAsync(planetId, receipt);
        return TaskResult.SuccessResult;
    }

    private async Task RestoreImportedVisibilityAsync(long planetId, FederatedImportReceipt receipt)
    {
        var planet = await _db.Planets.FindAsync(planetId);
        if (planet is null)
            return;

        planet.Public = receipt.SourcePublic;
        planet.Discoverable = receipt.SourceDiscoverable;
        planet.LockedForMigration = false;
        receipt.ConfirmedAt ??= DateTime.UtcNow;
        await _db.SaveChangesAsync();
        _hostedPlanetService.Remove(planetId);
    }

    private async Task LockPendingImportedPlanetAsync(long planetId)
    {
        var planet = await _db.Planets.FindAsync(planetId);
        if (planet is null || planet.LockedForMigration)
            return;

        planet.LockedForMigration = true;
        await _db.SaveChangesAsync();
        _hostedPlanetService.Remove(planetId);
    }

    private static PlanetSnapshot CloneForPendingImport(PlanetSnapshot snapshot)
    {
        var clone = System.Text.Json.JsonSerializer.Deserialize<PlanetSnapshot>(
            System.Text.Json.JsonSerializer.Serialize(snapshot));
        clone.Planet.Public = false;
        clone.Planet.Discoverable = false;
        return clone;
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

        var nodeDomain = stub.NodeDomain;
        var scheme = FederationConfig.Current?.AllowInsecure == true ? "http" : "https";
        var nodeBase = $"{scheme}://{nodeDomain}";

        // Node registration verified this domain once, but a later DNS change
        // must not turn an owner-requested pull-back into a hub-side SSRF. The
        // connect-time handler pins federation clients too; this explicit
        // check covers the stored node URL before we persist a migration state
        // or issue any S2S request to it. Development/LAN mode intentionally
        // opts out.
        if (FederationConfig.Current?.AllowInsecure != true &&
            !await OutboundUrlSafetyValidator.IsSafeAsync(nodeBase, _logger))
        {
            return TaskResult.FromFailure("Community node no longer resolves to a public address.");
        }

        var existingOfficial = await _db.Planets.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == planetId);
        var migration = await _db.FederatedMigrations.FindAsync(planetId);
        // A node records the grant id when it freezes the planet. Preserve that
        // id for *every* retry of a pending pull-back, not only after the hub
        // imported its copy. Otherwise a lost abort response can leave the node
        // frozen under the old id while the hub retries with a new one that the
        // node correctly rejects as a different migration.
        var resumingPullBack = migration?.Status == FederatedMigrationStatus.Pending &&
                              migration.TargetDomain == HostingConfig.Current.RootDomain;
        var resumingPurge = existingOfficial is not null && resumingPullBack;
        if (existingOfficial is not null && !resumingPurge)
            return TaskResult.FromFailure("A planet with this id already exists on official.");

        // Persist the pending pull-back before contacting the node. If the hub
        // imports successfully but loses the purge response, a later owner retry
        // sees this record and resumes only the idempotent purge/cleanup phase.
        migration ??= new FederatedMigration { PlanetId = planetId };
        if (_db.Entry(migration).State == EntityState.Detached)
            await _db.FederatedMigrations.AddAsync(migration);

        if (!resumingPullBack)
        {
            migration.TargetDomain = HostingConfig.Current.RootDomain;
            migration.Status = FederatedMigrationStatus.Pending;
            migration.CreatedAt = DateTime.UtcNow;
            migration.GrantId = Guid.NewGuid().ToString("N");
            migration.CompletedAt = null;
            await _db.SaveChangesAsync();
        }

        // A retry keeps the same grant id. The node records that id when it
        // freezes the planet, and a freshly minted JWT below gives the same
        // attempt a new expiry without creating a competing migration.
        if (string.IsNullOrWhiteSpace(migration.GrantId))
        {
            migration.GrantId = Guid.NewGuid().ToString("N");
            await _db.SaveChangesAsync();
        }

        var grant = await MintGrantAsync(planetId, nodeDomain, ownerId, migration.GrantId, PullbackPurpose);
        if (grant is null)
            return TaskResult.FromFailure("Could not sign the pull-back grant.");

        if (!resumingPurge)
        {
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
                    await AbortPullBackAttemptAsync(nodeBase, planetId, grant, migration);
                    return TaskResult.FromFailure($"Snapshot pull from node failed: {(int)resp.StatusCode}");
                }
                snapshot = await resp.Content.ReadFromJsonAsync<PlanetSnapshot>();
            }
            catch (Exception e)
            {
                await AbortPullBackAttemptAsync(nodeBase, planetId, grant, migration);
                return TaskResult.FromFailure($"Snapshot pull from node failed: {e.Message}");
            }

            if (snapshot?.Planet is null || snapshot.Planet.Id != planetId)
            {
                await AbortPullBackAttemptAsync(nodeBase, planetId, grant, migration);
                return TaskResult.FromFailure("Node snapshot did not match.");
            }

            // A registered node operator is the explicit trust authority for its
            // community's data. Mark the content as imported so that the trust is
            // visible to members and remains distinguishable from native history.
            var prepared = await PreparePulledBackSnapshotAsync(snapshot, planetId, ownerId, nodeDomain);
            if (!prepared.Success)
            {
                await AbortPullBackAttemptAsync(nodeBase, planetId, grant, migration);
                return prepared;
            }

            // Keep the official copy hidden until the node confirms its purge;
            // otherwise a failed purge creates two discoverable homes.
            snapshot.Planet.Public = false;
            snapshot.Planet.Discoverable = false;
            var import = await _snapshotService.ImportAsync(snapshot);
            if (!import.Success)
            {
                await AbortPullBackAttemptAsync(nodeBase, planetId, grant, migration);
                return import;
            }
        }

        var purge = await PurgePulledBackNodeAsync(nodeBase, planetId, grant);
        if (!purge.Success)
            return TaskResult.FromFailure($"Official import succeeded, but node purge is pending. Retry this pull-back: {purge.Message}");

        // The node has confirmed deletion. It is now safe to publish the
        // official copy and revoke the routing/membership records for the old
        // host in the same local save.
        var official = await _db.Planets.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == planetId);
        if (official is null)
            return TaskResult.FromFailure("The imported official planet is missing; registry state was retained.");

        official.Public = stub.Public;
        official.Discoverable = stub.Discoverable;
        await _db.FederatedMemberships
            .Where(x => x.PlanetId == planetId && x.NodeDomain == nodeDomain)
            .ExecuteDeleteAsync();
        await _db.FederatedInviteRedemptions.Where(x => x.PlanetId == planetId).ExecuteDeleteAsync();
        await _db.FederatedInviteGrants
            .Where(x => x.PlanetId == planetId && x.NodeDomain == nodeDomain)
            .ExecuteDeleteAsync();

        _db.FederatedPlanetStubs.Remove(stub);
        migration.Status = FederatedMigrationStatus.Completed;
        migration.CompletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        _hostedPlanetService.Remove(planetId);

        _logger.LogInformation("Planet {PlanetId} pulled back to official from {Domain}", planetId, nodeDomain);
        return TaskResult.SuccessResult;
    }

    private async Task<TaskResult> PurgePulledBackNodeAsync(string nodeBase, long planetId, string grant)
    {
        try
        {
            var client = _httpFactory.CreateClient("federation");
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{nodeBase}/api/federation/migrations/{planetId}/purge");
            req.Headers.Add("X-Valour-Migration-Grant", grant);
            using var resp = await client.SendAsync(req);
            return resp.IsSuccessStatusCode
                ? TaskResult.SuccessResult
                : TaskResult.FromFailure($"Node returned {(int)resp.StatusCode}.");
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Node purge after pull-back threw for {PlanetId}", planetId);
            return TaskResult.FromFailure(e.Message);
        }
    }

    /// <summary>
    /// Best-effort: tell the node to unfreeze a planet whose pull-back the hub
    /// could not finish, so a failed import doesn't leave it read-only.
    /// </summary>
    private async Task AbortPullBackAttemptAsync(
        string nodeBase,
        long planetId,
        string grant,
        FederatedMigration migration)
    {
        // If the node acknowledged the abort, give the next owner retry a new
        // jti. If the request or response was lost, intentionally retain this
        // pending state so the retry can resume the same frozen node attempt.
        if (!await TryAbortPullBackOnNodeAsync(nodeBase, planetId, grant))
            return;

        migration.Status = FederatedMigrationStatus.Aborted;
        migration.CompletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    private async Task<bool> TryAbortPullBackOnNodeAsync(string nodeBase, long planetId, string grant)
    {
        try
        {
            var client = _httpFactory.CreateClient("federation");
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{nodeBase}/api/federation/migrations/{planetId}/pullback-abort");
            req.Headers.Add("X-Valour-Migration-Grant", grant);
            using var resp = await client.SendAsync(req);
            if (resp.IsSuccessStatusCode)
                return true;

            _logger.LogWarning("Pull-back abort on node returned {Status} for {PlanetId}", (int)resp.StatusCode, planetId);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Pull-back abort on node threw for {PlanetId}", planetId);
        }

        return false;
    }

    /// <summary>
    /// Pull-back imports a community's data under the registered node owner's
    /// authority. The owner is known to the hub and is accountable for its
    /// node, so roles, memberships, moderation settings, media references, and
    /// user identity claims are preserved rather than silently downgraded.
    ///
    /// User-generated history is marked with immutable import provenance. This
    /// is deliberately generic so non-federation importers (for example a
    /// Discord importer) can use the same UI and moderation signal.
    /// </summary>
    public async Task<TaskResult> PreparePulledBackSnapshotAsync(
        PlanetSnapshot snapshot, long planetId, long planetOwnerId, string nodeDomain)
    {
        if (snapshot?.Planet is null || snapshot.Planet.Id != planetId)
            return TaskResult.FromFailure("Node snapshot did not match the pull-back request.");

        var nodeOwnerId = await _db.FederatedNodes.AsNoTracking()
            .Where(x => x.Domain == nodeDomain && x.Status == FederatedNodeStatus.Active)
            .Select(x => (long?)x.OwnerId)
            .FirstOrDefaultAsync();
        if (nodeOwnerId is null)
            return TaskResult.FromFailure("The community node is not an active, hub-registered node.");

        var ownerExists = await _db.Users.AsNoTracking()
            .AnyAsync(x => x.Id == nodeOwnerId.Value && !x.IsFederated);
        if (!ownerExists)
            return TaskResult.FromFailure("The community node's registered hub owner no longer exists.");

        var importSource = $"federation:{nodeDomain}";
        snapshot.SourceDomain = nodeDomain;
        // The hub's planet stub and the caller authorization remain the source
        // of truth for ownership. The registered node owner is trusted for the
        // community's contents and moderation state, not for silently handing
        // the planet to another hub account during pull-back.
        snapshot.Planet.OwnerId = planetOwnerId;

        // Overwrite any node-supplied provenance. The node controls its data,
        // but the hub controls the statement that it was imported from this
        // particular registered node.
        foreach (var message in snapshot.Messages ?? [])
            message.ImportSource = importSource;
        foreach (var attachment in snapshot.Attachments ?? [])
        {
            // This is a foreign key into the node's own CDN database, not the
            // media itself. Retain the location and all attachment metadata;
            // only remove the non-portable pointer so the hub can persist it.
            attachment.CdnBucketItemId = null;
        }
        foreach (var reaction in snapshot.Reactions ?? [])
            reaction.ImportSource = importSource;
        foreach (var thread in snapshot.Threads ?? [])
            thread.ImportSource = importSource;
        foreach (var comment in snapshot.ThreadComments ?? [])
            comment.ImportSource = importSource;
        foreach (var page in snapshot.WikiPages ?? [])
            page.ImportSource = importSource;
        foreach (var revision in snapshot.WikiRevisions ?? [])
            revision.ImportSource = importSource;

        _logger.LogInformation(
            "Prepared pull-back of planet {PlanetId} from {Domain}; data is authorized by registered node owner {NodeOwnerId}",
            planetId, nodeDomain, nodeOwnerId.Value);

        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// Node-side: serve a snapshot to the hub during a pull-back. Authorized by
    /// a hub-signed pull-back grant (validated against hub JWKS).
    /// </summary>
    public async Task<TaskResult<PlanetSnapshot>> ExportForPullBackAsync(long requestedPlanetId, string grant)
    {
        var (planetId, grantId) = await ValidatePullBackGrantAsync(grant);
        if (planetId is null || planetId.Value != requestedPlanetId)
            return TaskResult<PlanetSnapshot>.FromFailure("Invalid pull-back grant.");

        // Freeze the local planet read-only for the hub's import window so no
        // writes are lost between this export and the purge. Reversible via
        // AbortPullBackAsync if the hub's import fails.
        var planet = await _db.Planets.FindAsync(planetId.Value);
        if (planet is null)
            return TaskResult<PlanetSnapshot>.FromFailure("Planet not found.");

        var migration = await _db.FederatedMigrations.FindAsync(planetId.Value);
        if (migration is not null && migration.Status == FederatedMigrationStatus.Pending &&
            !string.Equals(migration.GrantId, grantId, StringComparison.Ordinal))
        {
            return TaskResult<PlanetSnapshot>.FromFailure("A different pull-back is already pending for this planet.");
        }

        if (migration?.Status == FederatedMigrationStatus.Completed)
            return TaskResult<PlanetSnapshot>.FromFailure("This pull-back has already completed.");

        // An aborted grant must never be allowed to re-freeze a planet. A new
        // hub grant has a different jti and deliberately starts a new attempt.
        if (migration?.Status == FederatedMigrationStatus.Aborted &&
            string.Equals(migration.GrantId, grantId, StringComparison.Ordinal))
        {
            return TaskResult<PlanetSnapshot>.FromFailure("This pull-back grant was aborted. Request a new grant from the hub.");
        }

        if (migration is null)
        {
            migration = new FederatedMigration { PlanetId = planetId.Value };
            await _db.FederatedMigrations.AddAsync(migration);
        }

        // This record lives on the community node. Keep the actual trusted hub
        // authority as metadata rather than this process's issuer (which is the
        // node's own domain in node mode).
        migration.TargetDomain = new Uri(FederationConfig.Current.HubUrl).Host;
        migration.Status = FederatedMigrationStatus.Pending;
        migration.GrantId = grantId;
        migration.CreatedAt = DateTime.UtcNow;
        migration.CompletedAt = null;

        if (!planet.LockedForMigration)
        {
            planet.LockedForMigration = true;
        }
        await _db.SaveChangesAsync();
        _hostedPlanetService.Remove(planetId.Value);

        var export = await _snapshotService.ExportAsync(planetId.Value);
        if (export.Success)
            return export;

        // A rejected export (for example, unportable media) must not leave a
        // usable capability holding the planet read-only. Mark this jti aborted
        // so a captured request cannot replay it after the owner resumes work.
        planet.LockedForMigration = false;
        migration.Status = FederatedMigrationStatus.Aborted;
        migration.CompletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        _hostedPlanetService.Remove(planetId.Value);
        return export;
    }

    /// <summary>
    /// Node-side: delete the local planet after the hub confirms it imported it.
    /// Only a planet that was frozen for export can be purged, so a bare grant
    /// replay can't destroy a live planet that never entered the pull-back flow.
    /// </summary>
    public async Task<TaskResult> PurgeForPullBackAsync(long requestedPlanetId, string grant)
    {
        var (planetId, grantId) = await ValidatePullBackGrantAsync(grant);
        if (planetId is null || planetId.Value != requestedPlanetId)
            return TaskResult.FromFailure("Invalid pull-back grant.");

        var migration = await _db.FederatedMigrations.FindAsync(planetId.Value);
        if (migration is null || !string.Equals(migration.GrantId, grantId, StringComparison.Ordinal))
            return TaskResult.FromFailure("This pull-back grant is no longer active.");
        if (migration.Status == FederatedMigrationStatus.Completed)
            return TaskResult.SuccessResult;
        if (migration.Status != FederatedMigrationStatus.Pending)
            return TaskResult.FromFailure("This pull-back was aborted.");

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
            migration.Status = FederatedMigrationStatus.Completed;
            migration.CompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            _hostedPlanetService.Remove(planetId.Value);
        }
        return result;
    }

    /// <summary>
    /// Node-side: unfreeze a planet whose pull-back the hub could not complete,
    /// so a failed import doesn't leave it read-only forever.
    /// </summary>
    public async Task<TaskResult> AbortPullBackAsync(long requestedPlanetId, string grant)
    {
        var (planetId, grantId) = await ValidatePullBackGrantAsync(grant);
        if (planetId is null || planetId.Value != requestedPlanetId)
            return TaskResult.FromFailure("Invalid pull-back grant.");

        var migration = await _db.FederatedMigrations.FindAsync(planetId.Value);
        if (migration is null || !string.Equals(migration.GrantId, grantId, StringComparison.Ordinal))
            return TaskResult.FromFailure("This pull-back grant is no longer active.");
        if (migration.Status == FederatedMigrationStatus.Completed)
            return TaskResult.FromFailure("This pull-back has already completed.");

        var planet = await _db.Planets.FindAsync(planetId.Value);
        if (planet is not null && planet.LockedForMigration)
        {
            planet.LockedForMigration = false;
        }

        migration.Status = FederatedMigrationStatus.Aborted;
        migration.CompletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        _hostedPlanetService.Remove(planetId.Value);

        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// Validates a hub-signed pull-back grant on the node: hub signature, our
    /// domain as audience, the pull-back purpose. Returns the planet id.
    /// </summary>
    private async Task<(long? planetId, string grantId)> ValidatePullBackGrantAsync(string grant)
    {
        var claims = await _nodeService.ValidateHubSignedTokenAsync(
            grant, FederationConfig.Current.NodeDomain);
        if (claims is null)
            return (null, null);

        if (!claims.TryGetValue("purpose", out var purpose) || purpose?.ToString() != PullbackPurpose)
            return (null, null);

        if (!claims.TryGetValue("planet", out var planetRaw) || !long.TryParse(planetRaw?.ToString(), out var planetId))
            return (null, null);
        if (!claims.TryGetValue("jti", out var grantRaw) || string.IsNullOrWhiteSpace(grantRaw?.ToString()))
            return (null, null);

        return (planetId, grantRaw.ToString());
    }

    // ===================== Grant crypto =====================

    private async Task<string> MintGrantAsync(
        long planetId,
        string targetDomain,
        long ownerId,
        string grantId,
        string purpose = GrantPurpose)
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
                ["jti"] = grantId,
                ["purpose"] = purpose,
                ["planet"] = planetId.ToString(),
                ["owner_id"] = ownerId.ToString(),
                ["protocol"] = ValourFederation.ProtocolVersion,
            },
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private static bool TryReadForwardGrant(
        IDictionary<string, object> claims,
        out long planetId,
        out long ownerId,
        out string grantId)
    {
        planetId = 0;
        ownerId = 0;
        grantId = null;
        return claims is not null &&
               claims.TryGetValue("protocol", out var protocol) &&
               int.TryParse(protocol?.ToString(), out var protocolVersion) &&
               protocolVersion == ValourFederation.ProtocolVersion &&
               claims.TryGetValue("purpose", out var purpose) && purpose?.ToString() == GrantPurpose &&
               claims.TryGetValue("planet", out var planet) && long.TryParse(planet?.ToString(), out planetId) &&
               claims.TryGetValue("owner_id", out var owner) && long.TryParse(owner?.ToString(), out ownerId) &&
               claims.TryGetValue("jti", out var id) && !string.IsNullOrWhiteSpace(grantId = id?.ToString());
    }

    /// <summary>
    /// Validates a migration grant against the hub's own signing keys (the hub
    /// minted it). Returns the capability claims when valid.
    /// </summary>
    private async Task<(long? planetId, string target, string grantId, long? ownerId, string purpose)> ValidateGrantAsync(string grant)
    {
        if (string.IsNullOrWhiteSpace(grant))
            return (null, null, null, null, null);

        var jwks = await _keyService.GetJwksJsonAsync();
        JsonWebKeySet keySet;
        try
        {
            keySet = new JsonWebKeySet(jwks);
        }
        catch
        {
            return (null, null, null, null, null);
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
            return (null, null, null, null, null);

        if (!result.Claims.TryGetValue("protocol", out var protocolRaw) ||
            !int.TryParse(protocolRaw?.ToString(), out var protocolVersion) ||
            protocolVersion != ValourFederation.ProtocolVersion ||
            !result.Claims.TryGetValue("purpose", out var purpose) ||
            !result.Claims.TryGetValue("planet", out var planetRaw) || !long.TryParse(planetRaw?.ToString(), out var planetId) ||
            !result.Claims.TryGetValue("jti", out var grantRaw) || string.IsNullOrWhiteSpace(grantRaw?.ToString()) ||
            !result.Claims.TryGetValue("owner_id", out var ownerRaw) || !long.TryParse(ownerRaw?.ToString(), out var ownerId))
        {
            return (null, null, null, null, null);
        }

        var target = result.Claims.TryGetValue("aud", out var aud) ? aud?.ToString() : null;
        return (planetId, target, grantRaw.ToString(), ownerId, purpose?.ToString());
    }
}
