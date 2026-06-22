using Valour.Server.Database;
using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Server.Services;

public class PermissionsNodeService
{
    private readonly ValourDb _db;
    private readonly CoreHubService _coreHub;
    private readonly ILogger<PermissionsNodeService> _logger;
    private readonly PlanetPermissionService _permissionService;
    private readonly HostedPlanetService _hostedPlanetService;

    public PermissionsNodeService(
        ValourDb db,
        CoreHubService coreHub,
        ILogger<PermissionsNodeService> logger,
        PlanetPermissionService permissionService,
        HostedPlanetService hostedPlanetService)
    {
        _db = db;
        _coreHub = coreHub;
        _logger = logger;
        _permissionService = permissionService;
        _hostedPlanetService = hostedPlanetService;
    }

    /// <summary>
    /// Returns the user ids of planet members who can currently view the channels affected by
    /// a node targeting the given channel id (the channel itself, plus anything inheriting its
    /// permissions). Used to snapshot access before a node changes, so the after-state can be
    /// diffed to find members who lost access.
    /// </summary>
    private async Task<Dictionary<long, List<long>>> SnapshotAffectedChannelViewersAsync(
        HostedPlanet hostedPlanet,
        long targetChannelId)
    {
        var affectedChannelIds = new List<long> { targetChannelId };
        var inheritors = hostedPlanet.GetInheritors(targetChannelId);
        if (inheritors is not null)
            affectedChannelIds.AddRange(inheritors);

        var snapshot = new Dictionary<long, List<long>>(affectedChannelIds.Count);
        foreach (var channelId in affectedChannelIds)
        {
            snapshot[channelId] = await _permissionService.GetChannelViewerUserIdsAsync(hostedPlanet, channelId);
        }

        return snapshot;
    }

    /// <summary>
    /// Diffs the given pre-change viewer snapshot against current access and, for any member who
    /// lost view access to a channel, evicts their already-joined connections from that channel's
    /// real-time message group and tells their client the channel is gone. Permission changes don't
    /// otherwise affect an already-joined SignalR group, so without this a member who revokes a
    /// role's view of a channel would keep receiving live messages for it.
    /// </summary>
    private async Task RevokeLostChannelAccessAsync(HostedPlanet hostedPlanet, Dictionary<long, List<long>> beforeViewers)
    {
        foreach (var (channelId, before) in beforeViewers)
        {
            var channel = hostedPlanet.GetChannel(channelId);
            if (channel is null)
                continue;

            var after = await _permissionService.GetChannelViewerUserIdsAsync(hostedPlanet, channelId);
            var revoked = before.Except(after).ToList();
            if (revoked.Count == 0)
                continue;

            await _coreHub.EvictUsersFromChannelGroupAsync(channelId, revoked);
            _coreHub.NotifyChannelDelete(channel, revoked);
        }
    }

    /// <summary>
    /// Returns the permission node for the given id
    /// </summary>
    public async Task<PermissionsNode> GetAsync(long id) =>
        (await _db.PermissionsNodes.FindAsync(id)).ToModel();

    public async Task<PermissionsNode> GetAsync(long? targetId, long roleId, ChannelTypeEnum type) =>
        (await _db.PermissionsNodes.FirstOrDefaultAsync(x => x.TargetId == targetId && x.RoleId == roleId && x.TargetType == type)).ToModel();

    public async Task<List<PermissionsNode>> GetAllAsync(long planetId) =>
        (await _db.PermissionsNodes.Where(x => x.PlanetId == planetId).Select(x => x.ToModel()).ToListAsync());

    public async Task<TaskResult<PermissionsNode>> PutAsync(PermissionsNode newNode)
    {
        var hostedPlanet = await _hostedPlanetService.GetRequiredAsync(newNode.PlanetId);
        var beforeViewers = await SnapshotAffectedChannelViewersAsync(hostedPlanet, newNode.TargetId);

        await using var trans = await _db.Database.BeginTransactionAsync();

        try
        {
            var oldNode = await _db.PermissionsNodes.FindAsync(newNode.Id);

            if (oldNode is null) return new(false, $"PermissionNode not found");
            _db.Entry(oldNode).CurrentValues.SetValues(newNode);
            await _db.SaveChangesAsync();

            await trans.CommitAsync();

        }
        catch (System.Exception e)
        {
            await trans.RollbackAsync();
            _logger.LogError("Error updating permissions node in DB: {Error}", e.Message);
            return new(false, e.Message);
        }

        // Update permissions
        await _permissionService.HandleNodeChange(newNode);

        await RevokeLostChannelAccessAsync(hostedPlanet, beforeViewers);

        _coreHub.NotifyPlanetItemChange(newNode);

        return new(true, "Success", newNode);
    }

    public async Task<TaskResult<PermissionsNode>> CreateAsync(PermissionsNode node)
    {
        node.Id = IdManager.Generate();

        var hostedPlanet = await _hostedPlanetService.GetRequiredAsync(node.PlanetId);
        var beforeViewers = await SnapshotAffectedChannelViewersAsync(hostedPlanet, node.TargetId);

        try
        {
            _db.PermissionsNodes.Add(node.ToDatabase());
            await _db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            _logger.LogError("Error saving permissions node to DB: {Error}", e.Message);
            return new(false, e.Message);
        }

        // Update permissions
        await _permissionService.HandleNodeChange(node);

        await RevokeLostChannelAccessAsync(hostedPlanet, beforeViewers);

        _coreHub.NotifyPlanetItemChange(node);

        return new(true, "Success", node);
    }
}
