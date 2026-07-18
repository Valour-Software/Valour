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
        if (request is null)
            return TaskResult<FederatedPlanetStubResponse>.FromFailure("Include request in body.");

        return await CreateStubAsync(nodeDomain, IdManager.Generate(), request);
    }

    /// <summary>
    /// Registers a stub at a specific existing id — used when a node adopts a
    /// planet id it already owns: a planet migrating from the official network
    /// (which keeps its id) or an edge-generated snowflake. Rejects an id that
    /// already belongs to a different node.
    /// </summary>
    public async Task<TaskResult<FederatedPlanetStubResponse>> AdoptAsync(string nodeDomain, long id, FederatedPlanetStubRequest request)
    {
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

        // Guard against colliding with a live official planet id.
        if (await _db.Planets.AnyAsync(x => x.Id == id))
            return TaskResult<FederatedPlanetStubResponse>.FromFailure("This planet id belongs to an official planet; migrate it instead.");

        return await CreateStubAsync(nodeDomain, id, request);
    }

    private async Task<TaskResult<FederatedPlanetStubResponse>> CreateStubAsync(string nodeDomain, long id, FederatedPlanetStubRequest request)
    {
        var stub = new Valour.Database.FederatedPlanetStub
        {
            Id = id,
            NodeDomain = nodeDomain,
            Name = request.Name,
            Description = request.Description,
            OwnerId = request.OwnerId,
            MemberCount = Math.Max(0, request.MemberCount),
            Nsfw = request.Nsfw,
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
        if (request is null)
            return TaskResult<FederatedPlanetStubResponse>.FromFailure("Include request in body.");

        var stub = await _db.FederatedPlanetStubs.FindAsync(id);
        if (stub is null)
            return TaskResult<FederatedPlanetStubResponse>.FromFailure("Planet stub not found. Reserve an id first.");

        if (stub.NodeDomain != nodeDomain)
            return TaskResult<FederatedPlanetStubResponse>.FromFailure("This planet is hosted by a different node.");

        stub.Name = request.Name;
        stub.Description = request.Description;
        stub.OwnerId = request.OwnerId;
        stub.MemberCount = Math.Max(0, request.MemberCount);
        stub.Nsfw = request.Nsfw;
        stub.Discoverable = request.Discoverable;
        stub.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return TaskResult<FederatedPlanetStubResponse>.FromData(ToResponse(stub));
    }

    public async Task<TaskResult> DeleteAsync(string nodeDomain, long id)
    {
        var stub = await _db.FederatedPlanetStubs.FindAsync(id);
        if (stub is null)
            return TaskResult.SuccessResult;

        if (stub.NodeDomain != nodeDomain)
            return TaskResult.FromFailure("This planet is hosted by a different node.");

        _db.FederatedPlanetStubs.Remove(stub);
        await _db.SaveChangesAsync();

        return TaskResult.SuccessResult;
    }

    private static FederatedPlanetStubResponse ToResponse(Valour.Database.FederatedPlanetStub stub) => new()
    {
        Id = stub.Id,
        NodeDomain = stub.NodeDomain,
        Name = stub.Name,
        Discoverable = stub.Discoverable,
    };
}
