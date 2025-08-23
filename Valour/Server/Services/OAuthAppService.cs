using Valour.Shared;

namespace Valour.Server.Services;

public class OauthAppService
{
    private readonly ValourDb _db;
    private readonly ILogger<OauthAppService> _logger;

    public OauthAppService(
        ValourDb db,
        ILogger<OauthAppService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<OauthApp> GetAsync(long id) =>
        (await _db.OauthApps.FindAsync(id)).ToModel();

    public async Task<TaskResult<OauthApp>> UpdateAsync(OauthApp updatedApp)
    {
        var old = await _db.OauthApps.FindAsync(updatedApp.Id);

        if (old == null)
            return new(false, "App not found");

        old.RedirectUrl = updatedApp.RedirectUrl;

        try
        {
            _db.OauthApps.Update(old);
            await _db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e.Message);
            return new(false, e.Message);
        }

        return new(true, "Success", old.ToModel());
    }

    public async Task<bool> OwnsAppAsync(long userId, long appId) =>
        await _db.OauthApps.AnyAsync(a => a.OwnerId == userId && a.Id == appId);
}