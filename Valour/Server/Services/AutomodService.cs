using Valour.Server.Database;
using Valour.Shared;
using Valour.Shared.Queries;
using Valour.Server.Mapping;
using Valour.Server.Models;

namespace Valour.Server.Services;

public class AutomodService
{
    private readonly ValourDb _db;
    private readonly ILogger<AutomodService> _logger;
    private readonly CoreHubService _coreHub;

    public AutomodService(
        ValourDb db,
        ILogger<AutomodService> logger,
        CoreHubService coreHub)
    {
        _db = db;
        _logger = logger;
        _coreHub = coreHub;
    }

    public async Task<AutomodTrigger?> GetTriggerAsync(Guid id) =>
        (await _db.AutomodTriggers.FindAsync(id))?.ToModel();

    public async Task<List<AutomodTrigger>> GetPlanetTriggersAsync(long planetId) =>
        await _db.AutomodTriggers.Where(x => x.PlanetId == planetId)
            .Select(x => x.ToModel()).ToListAsync();

    public async Task<QueryResponse<AutomodTrigger>> QueryPlanetTriggersAsync(long planetId, QueryRequest request)
    {
        var take = Math.Min(50, request.Take);
        var skip = request.Skip;
        var query = _db.AutomodTriggers.Where(x => x.PlanetId == planetId).AsQueryable();
        var total = await query.CountAsync();
        var items = await query.Skip(skip).Take(take).Select(x => x.ToModel()).ToListAsync();
        return new QueryResponse<AutomodTrigger>
        {
            Items = items,
            TotalCount = total
        };
    }

    public async Task<TaskResult<AutomodTrigger>> CreateTriggerAsync(AutomodTrigger trigger)
    {
        trigger.Id = Guid.NewGuid();
        try
        {
            await _db.AutomodTriggers.AddAsync(trigger.ToDatabase());
            await _db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e.Message);
            return new(false, e.Message);
        }

        _coreHub.NotifyPlanetItemChange(trigger);
        return new(true, "Success", trigger);
    }

    public async Task<TaskResult> DeleteTriggerAsync(AutomodTrigger trigger)
    {
        try
        {
            var dbItem = await _db.AutomodTriggers.FindAsync(trigger.Id);
            if (dbItem != null)
            {
                _db.AutomodTriggers.Remove(dbItem);
                await _db.SaveChangesAsync();
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e.Message);
            return new(false, e.Message);
        }

        _coreHub.NotifyPlanetItemDelete(trigger);
        return new(true, "Success");
    }

    public async Task<TaskResult<AutomodAction>> CreateActionAsync(AutomodAction action)
    {
        action.Id = Guid.NewGuid();
        try
        {
            await _db.AutomodActions.AddAsync(action.ToDatabase());
            await _db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e.Message);
            return new(false, e.Message);
        }

        _coreHub.NotifyPlanetItemChange(action.PlanetId, action);
        return new(true, "Success", action);
    }
}
