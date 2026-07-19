using Valour.Server.Database;
using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Server.Services;

/// <summary>
/// Hub-side registry of planets hosted on community nodes. The hub mints the
/// planet id (preserving the one global snowflake id space) and keeps a stub
/// for discovery, invites, and moderation. Only the owning node may write a
/// given stub.
/// </summary>
public class FederationPlanetRegistryService
{
    private readonly ValourDb _db;

    public FederationPlanetRegistryService(ValourDb db)
    {
        _db = db;
    }

    /// <summary>
    /// Reserves a fresh planet id for a node and stores the initial stub.
    /// For brand-new community planets that don't already have an id.
    /// </summary>
    public async Task<TaskResult<FederatedPlanetStubResponse>> ReserveAsync(string nodeDomain, FederatedPlanetStubRequest request)
    {
        if (!FederationHubService.HubEnabled)
            return TaskResult<FederatedPlanetStubResponse>.FromFailure("This instance is not a federation hub.");

        if (request is null)
            return TaskResult<FederatedPlanetStubResponse>.FromFailure("Include request in body.");

        var node = await _db.FederatedNodes.AsNoTracking().FirstOrDefaultAsync(x => x.Domain == nodeDomain);
        if (node is null || node.Status != Valour.Database.FederatedNodeStatus.Active)
            return TaskResult<FederatedPlanetStubResponse>.FromFailure("Only active community nodes can reserve planets.");

        // A node is not allowed to nominate an arbitrary hub user as the owner
        // of a brand-new planet. The accountable owner is the account that
        // registered the node; official-to-node migrations create their own
        // stub through the migration protocol and preserve the real owner.
        return await CreateStubAsync(nodeDomain, IdManager.Generate(), request, node.OwnerId);
    }

    /// <summary>
    /// Registers a stub at a specific existing id — used when a node adopts a
    /// planet id it already owns. A new arbitrary id cannot be adopted: without
    /// a hub-issued reservation or an active migration it could collide with a
    /// future global id. Official-to-node migration stubs are created by the
    /// migration protocol, so this endpoint is idempotent-only.
    /// </summary>
    public async Task<TaskResult<FederatedPlanetStubResponse>> AdoptAsync(string nodeDomain, long id, FederatedPlanetStubRequest request)
    {
        if (!FederationHubService.HubEnabled)
            return TaskResult<FederatedPlanetStubResponse>.FromFailure("This instance is not a federation hub.");

        if (request is null)
            return TaskResult<FederatedPlanetStubResponse>.FromFailure("Include request in body.");

        if (id <= 0)
            return TaskResult<FederatedPlanetStubResponse>.FromFailure("A planet id is required to adopt.");

        var existing = await _db.FederatedPlanetStubs.FindAsync(id);
        if (existing is not null)
        {
            if (existing.NodeDomain != nodeDomain)
                return TaskResult<FederatedPlanetStubResponse>.FromFailure("This planet id is already registered to another node.");

            // Idempotent re-adopt by the same node → just update.
            return await UpsertAsync(nodeDomain, id, request);
        }

        return TaskResult<FederatedPlanetStubResponse>.FromFailure(
            "Planet ids must be reserved by the hub. Existing migration stubs may be updated, but arbitrary ids cannot be adopted.");
    }

    private async Task<TaskResult<FederatedPlanetStubResponse>> CreateStubAsync(
        string nodeDomain, long id, FederatedPlanetStubRequest request, long ownerId)
    {
        var stub = new Valour.Database.FederatedPlanetStub
        {
            Id = id,
            NodeDomain = nodeDomain,
            Name = request.Name,
            Description = request.Description,
            OwnerId = ownerId,
            MemberCount = Math.Max(0, request.MemberCount),
            Nsfw = request.Nsfw,
            Public = request.Public,
            Discoverable = request.Discoverable,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        await _db.FederatedPlanetStubs.AddAsync(stub);
        await _db.SaveChangesAsync();

        return TaskResult<FederatedPlanetStubResponse>.FromData(ToResponse(stub));
    }

    /// <summary>
    /// Updates an existing stub. The stub must belong to the calling node.
    /// </summary>
    public async Task<TaskResult<FederatedPlanetStubResponse>> UpsertAsync(string nodeDomain, long id, FederatedPlanetStubRequest request)
    {
        if (!FederationHubService.HubEnabled)
            return TaskResult<FederatedPlanetStubResponse>.FromFailure("This instance is not a federation hub.");

        if (request is null)
            return TaskResult<FederatedPlanetStubResponse>.FromFailure("Include request in body.");

        var stub = await _db.FederatedPlanetStubs.FindAsync(id);
        if (stub is null)
            return TaskResult<FederatedPlanetStubResponse>.FromFailure("Planet stub not found. Reserve an id first.");

        if (stub.NodeDomain != nodeDomain)
            return TaskResult<FederatedPlanetStubResponse>.FromFailure("This planet is hosted by a different node.");

        stub.Name = request.Name;
        stub.Description = request.Description;
        // OwnerId is immutable after reservation. Allowing a node to rewrite it
        // would let the node impersonate any hub account in global discovery and
        // later pull-back authorization.
        stub.MemberCount = Math.Max(0, request.MemberCount);
        stub.Nsfw = request.Nsfw;
        stub.Public = request.Public;
        stub.Discoverable = request.Discoverable;
        stub.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return TaskResult<FederatedPlanetStubResponse>.FromData(ToResponse(stub));
    }

    public async Task<TaskResult> DeleteAsync(string nodeDomain, long id)
    {
        if (!FederationHubService.HubEnabled)
            return TaskResult.FromFailure("This instance is not a federation hub.");

        var stub = await _db.FederatedPlanetStubs.FindAsync(id);
        if (stub is null)
            return TaskResult.SuccessResult;

        if (stub.NodeDomain != nodeDomain)
            return TaskResult.FromFailure("This planet is hosted by a different node.");

        // A removed stub must also revoke the hub grants that point to it. If
        // they survived, a former member could keep minting a node session for
        // this domain despite the planet no longer existing in the registry.
        await _db.FederatedMemberships
            .Where(x => x.PlanetId == id && x.NodeDomain == nodeDomain)
            .ExecuteDeleteAsync();

        await _db.FederatedInviteRedemptions
            .Where(x => x.PlanetId == id)
            .ExecuteDeleteAsync();
        await _db.FederatedInviteGrants
            .Where(x => x.PlanetId == id && x.NodeDomain == nodeDomain)
            .ExecuteDeleteAsync();

        _db.FederatedPlanetStubs.Remove(stub);
        await _db.SaveChangesAsync();

        return TaskResult.SuccessResult;
    }

    private static FederatedPlanetStubResponse ToResponse(Valour.Database.FederatedPlanetStub stub) => new()
    {
        Id = stub.Id,
        NodeDomain = stub.NodeDomain,
        Name = stub.Name,
        Public = stub.Public,
        Discoverable = stub.Discoverable,
    };
}
