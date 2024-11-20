using Valour.Server.Database;
using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Server.Services;

public class PermissionsNodeService
{
    private readonly ValourDb _db;
    private readonly CoreHubService _coreHub;
    private readonly TokenService _tokenService;
    private readonly PlanetMemberService _memberService;
    private readonly ILogger<PermissionsNodeService> _logger;
    private readonly ChannelAccessService _accessService;

    public PermissionsNodeService(
        ValourDb db,
        CoreHubService coreHub,
        TokenService tokenService,
        PlanetMemberService memberService,
        ILogger<PermissionsNodeService> logger,
        ChannelAccessService accessService)
    {
        _db = db;
        _coreHub = coreHub;
        _tokenService = tokenService;
        _memberService = memberService;
        _logger = logger;
        _accessService = accessService;
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
        await using var trans = await _db.Database.BeginTransactionAsync();
        
        try
        {
            var _old = await _db.PermissionsNodes.FindAsync(newNode.Id);
            
            if (_old is null) return new(false, $"PermissionNode not found");
            _db.Entry(_old).CurrentValues.SetValues(newNode);
            await _db.SaveChangesAsync();
            
            // Recalculate access
            await _accessService.UpdateAllChannelAccessForChannel(newNode.TargetId);

            await _db.SaveChangesAsync();

            await trans.CommitAsync();

        }
        catch (System.Exception e)
        {
            await trans.RollbackAsync();
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

        return new(true, "Success", node);
    }
}