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

    private const int MaxRedirectUrlLength = 2048;

    /// <summary>
    /// Checks a redirect URL an app may send authorization codes to. It must be
    /// an absolute https URL (plain http only for loopback development
    /// servers) without a fragment or embedded credentials, so a code cannot be
    /// delivered in cleartext or to a host other than the one shown.
    /// </summary>
    public static TaskResult ValidateRedirectUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return TaskResult.FromFailure("Redirect URL is required.");

        if (url.Length > MaxRedirectUrlLength)
            return TaskResult.FromFailure($"Redirect URL must be at most {MaxRedirectUrlLength} characters.");

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return TaskResult.FromFailure("Redirect URL must be an absolute URL.");

        if (url.Contains('#'))
            return TaskResult.FromFailure("Redirect URL cannot contain a fragment.");

        if (!string.IsNullOrEmpty(uri.UserInfo))
            return TaskResult.FromFailure("Redirect URL cannot contain a username or password.");

        if (uri.Scheme == Uri.UriSchemeHttps)
            return TaskResult.SuccessResult;

        if (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)
            return TaskResult.SuccessResult;

        return TaskResult.FromFailure("Redirect URL must use https (http is only allowed for localhost).");
    }

    public async Task<TaskResult<OauthApp>> CreateAsync(OauthApp app, long ownerId)
    {
        // Apps are created before a redirect URL is set, so an empty one is
        // allowed here; authorization refuses apps without a valid URL.
        if (string.IsNullOrWhiteSpace(app.RedirectUrl))
        {
            app.RedirectUrl = string.Empty;
        }
        else
        {
            var redirectValid = ValidateRedirectUrl(app.RedirectUrl);
            if (!redirectValid.Success)
                return new(false, redirectValid.Message);
        }

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

        if (string.IsNullOrWhiteSpace(updatedApp.RedirectUrl))
        {
            updatedApp.RedirectUrl = string.Empty;
        }
        else
        {
            var redirectValid = ValidateRedirectUrl(updatedApp.RedirectUrl);
            if (!redirectValid.Success)
                return new(false, redirectValid.Message);
        }

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
