using System.Security.Cryptography;
using Valour.Server.Database;
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

    public async Task<OauthApp> GetAsync(long id)
    {
        var dbApp = await _db.OauthApps.FindAsync(id);
        return dbApp?.ToModel();
    }

    public async Task<List<OauthApp>> GetAllByOwnerAsync(long userId)
    {
        var dbApps = await _db.OauthApps
            .Where(x => x.OwnerId == userId)
            .ToListAsync();

        return dbApps.Select(x => x.ToModel()).ToList();
    }

    public async Task<TaskResult<OauthApp>> CreateAsync(OauthApp app, long ownerId)
    {
        if (app.RedirectUrl is null)
            app.RedirectUrl = string.Empty;

        if (await _db.OauthApps.CountAsync(x => x.OwnerId == ownerId) > 9)
            return new(false, "There is currently a 10 app limit!");

        var nameValid = PlanetService.ValidateName(app.Name);
        if (!nameValid.Success)
            return new(false, nameValid.Message);

        app.OwnerId = ownerId;
        app.Uses = 0;
        app.ImageUrl = "../_content/Valour.Client/media/logo/logo-512.png";
        app.Secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        app.Id = IdManager.Generate();

        _db.OauthApps.Add(app.ToDatabase());
        await _db.SaveChangesAsync();

        return new(true, "Success", app);
    }

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

    public async Task<TaskResult> DeleteAsync(long appId)
    {
        var app = await _db.OauthApps.FindAsync(appId);
        if (app is null)
            return new(false, "App not found");

        _db.OauthApps.Remove(app);
        await _db.SaveChangesAsync();

        return new(true, "Success");
    }

    public async Task<bool> OwnsAppAsync(long userId, long appId) =>
        await _db.OauthApps.AnyAsync(a => a.OwnerId == userId && a.Id == appId);
}
