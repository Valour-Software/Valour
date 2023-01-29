using Microsoft.EntityFrameworkCore.Storage;
using System.Security.Cryptography;
using Valour.Api.Nodes;
using Valour.Server.Database;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Server.Services;

public class PermissionsNodeService
{
    private readonly ValourDB _db;
    private readonly CoreHubService _coreHub;
    private readonly TokenService _tokenService;
    private readonly PlanetMemberService _memberService;
    private readonly ILogger<PermissionsNodeService> _logger;

    public PermissionsNodeService(
        ValourDB db,
        CoreHubService coreHub,
        TokenService tokenService,
        PlanetMemberService memberService,
        ILogger<PermissionsNodeService> logger)
    {
        _db = db;
        _coreHub = coreHub;
        _tokenService = tokenService;
        _memberService = memberService;
        _logger = logger;
    }

    /// <summary>
    /// Returns the permission node for the given id
    /// </summary>
    public async Task<PermissionsNode> GetAsync(long id) =>
        (await _db.PermissionsNodes.FindAsync(id)).ToModel();

    public async Task<PermissionsNode> GetAsync(long targetId, long roleId, PermissionsTargetType type) =>
        (await _db.PermissionsNodes.FirstOrDefaultAsync(x => x.TargetId == targetId && x.RoleId == roleId && x.TargetType == type)).ToModel();

    public async Task<TaskResult<PermissionsNode>> PutAsync(PermissionsNode oldNode, PermissionsNode newNode)
    {
        try
        {
            _db.Entry(_db.Find<Valour.Database.PermissionsNode>(oldNode.Id)).State = EntityState.Detached;
            _db.PermissionsNodes.Update(newNode.ToDatabase());
            await _db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            _logger.LogError(e.Message);
            return new(false, e.Message);
        }

        _coreHub.NotifyPlanetItemChange(newNode);

        return new(true, "Success", newNode);
    }
    
    public async Task<TaskResult<PermissionsNode>> CreateAsync(PermissionsNode node)
    {
        node.Id = IdManager.Generate();

        try
        {
            _db.PermissionsNodes.Add(node.ToDatabase());
            await _db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            _logger.LogError(e.Message);
            return new(false, e.Message);
        }

        _coreHub.NotifyPlanetItemChange(node);

        return new(true, "Success");
    }
}