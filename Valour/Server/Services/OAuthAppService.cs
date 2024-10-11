using Valour.Shared;

namespace Valour.Server.Services;

public class OauthAppService
{
    private readonly ValourDB _db;
    private readonly ILogger<OauthAppService> _logger;

    public OauthAppService(
        ValourDB db,
        ILogger<OauthAppService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<OauthApp> GetAsync(long id) =>
        (await _db.OauthApps.FindAsync(id)).ToModel();

    public async Task<TaskResult<OauthApp>> PutAsync(OauthApp old, OauthApp updatedApp)
    {
        old.RedirectUrl = updatedApp.RedirectUrl;

        try
        {
            _db.OauthApps.Update(old.ToDatabase());
            await _db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e.Message);
            return new(false, e.Message);
        }

        return new(true, "Success", old);
    }
}