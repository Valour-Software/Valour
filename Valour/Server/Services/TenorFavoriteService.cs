using Valour.Server.Database;
using Valour.Server.Hubs;
using Valour.Shared;

namespace Valour.Server.Services;

public class TenorFavoriteService
{
    private readonly ValourDB _db;
    private readonly ILogger<TenorFavoriteService> _logger;

    public TenorFavoriteService(
        ValourDB db,
        ILogger<TenorFavoriteService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<TenorFavorite> GetAsync(long id) =>
        (await _db.TenorFavorites.FindAsync(id)).ToModel();

    public async Task<TaskResult<TenorFavorite>> CreateAsync(TenorFavorite tenorFavorite)
    {
        tenorFavorite.Id = IdManager.Generate();
        try
        {
            _db.TenorFavorites.Add(tenorFavorite.ToDatabase());
            await _db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            _logger.LogError(e.Message);
            return new(false, e.Message);
        }

        return new(true, "Success", tenorFavorite);
;   }

    public async Task<TaskResult> DeleteAsync(TenorFavorite tenorFavorite)
    {
        try
        {
            _db.TenorFavorites.Remove(tenorFavorite.ToDatabase());
            await _db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            _logger.LogError(e.Message);
            return new(false, e.Message);
        }

        return new(true, "Success");
    }
}