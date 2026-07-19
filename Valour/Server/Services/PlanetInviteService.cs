using Valour.Server.Database;
using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Server.Services;

public class PlanetInviteService
{
    private readonly ValourDb _db;
    private readonly ILogger<PlanetInviteService> _logger;
    private readonly CoreHubService _coreHub;
    private readonly PlanetService _planetService;

    public PlanetInviteService(
        ValourDb db,
        ILogger<PlanetInviteService> logger,
        CoreHubService coreHub, 
        PlanetService planetService)
    {
        _db = db;
        _logger = logger;
        _coreHub = coreHub;
        _planetService = planetService;
    }

    public async Task<PlanetInvite> GetAsync(long id) => 
        (await _db.PlanetInvites.FindAsync(id)).ToModel();

    public async Task<PlanetInvite> GetAsync(string code) =>
        (await _db.PlanetInvites.FirstOrDefaultAsync(x => x.Id == code))
        .ToModel();

    public async Task<PlanetInvite> GetAsync(string code, long planetId) => 
        (await _db.PlanetInvites.FirstOrDefaultAsync(x => x.Id == code 
                                                                 && x.PlanetId == planetId))
        .ToModel();
    
    public async Task<TaskResult<PlanetInvite>> CreateAsync(PlanetInvite invite, PlanetMember issuer)
    {
        var migrationGuard = await MigrationLock.GuardAsync(_db, issuer?.PlanetId);
        if (!migrationGuard.Success)
            return new(false, migrationGuard.Message);

        invite.Id = await GenerateCodeAsync();
        invite.IssuerId = issuer.UserId;
        invite.TimeCreated = DateTime.UtcNow;
        invite.PlanetId = issuer.PlanetId;

        try
        {
            await _db.AddAsync(invite.ToDatabase());
            await _db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            _logger.LogError(e.Message);
            return new(false, e.Message);
        }

        _coreHub.NotifyPlanetItemChange(invite);

        return new(true, "Success", invite);
    }

    public async Task<TaskResult<PlanetInvite>> UpdateAsync(PlanetInvite updatedInvite)
    {
        var migrationGuard = await MigrationLock.GuardAsync(_db, updatedInvite?.PlanetId);
        if (!migrationGuard.Success)
            return new(false, migrationGuard.Message);

        var old = await _db.PlanetInvites.FindAsync(updatedInvite.Id);
        if (old is null) return new(false, $"PlanetInvite not found");

        var persistedMigrationGuard = await MigrationLock.GuardAsync(_db, old.PlanetId);
        if (!persistedMigrationGuard.Success)
            return new(false, persistedMigrationGuard.Message);

        if (updatedInvite.Id != old.Id)
            return new(false, "You cannot change the code.");
        if (updatedInvite.IssuerId != old.IssuerId)
            return new(false, "You cannot change who issued.");
        if (updatedInvite.TimeCreated != old.TimeCreated)
            return new(false, "You cannot change the creation time.");
        if (updatedInvite.PlanetId != old.PlanetId)
            return new(false, "You cannot change what planet.");
        try
        {
            _db.Entry(old).CurrentValues.SetValues(updatedInvite);
            await _db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            _logger.LogError(e.Message);
            return new(false, e.Message);
        }

        _coreHub.NotifyPlanetItemChange(updatedInvite);

        return new(true, "Success", updatedInvite);
    }
    
    public async Task<TaskResult> DeleteAsync(PlanetInvite invite)
    {
        if (invite is null || string.IsNullOrWhiteSpace(invite.Id))
            return TaskResult.FromFailure("Invite is required.");

        var storedInvite = await _db.PlanetInvites.FindAsync(invite.Id);
        if (storedInvite is null)
            return TaskResult.FromFailure("Invite not found or already deleted.");

        // The request model is client-controlled. Always derive the lock
        // target and the notification payload from the persisted invite so a
        // caller cannot bypass a migration lock by supplying another planet id.
        var migrationGuard = await MigrationLock.GuardAsync(_db, storedInvite.PlanetId);
        if (!migrationGuard.Success)
            return migrationGuard;

        try
        {
            var deleted = await _db.PlanetInvites.Where(x => x.Id == storedInvite.Id).ExecuteDeleteAsync();
            if (deleted == 0)
                return new(false, "Invite not found or already deleted.");
        }
        catch (System.Exception e)
        {
            _logger.LogError(e.Message);
            return new(false, e.Message);
        }

        _coreHub.NotifyPlanetItemDelete(storedInvite.ToModel());

        return TaskResult.SuccessResult;
    }
    
    public async Task<TaskResult<ISharedPlanetListInfo>> GetPlanetInfoByInviteCode(string inviteCode)
    {
        var invite = await _db.PlanetInvites.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == inviteCode);
        
        if (invite is null)
            return new TaskResult<ISharedPlanetListInfo>(false, "Invite not found.");
        
        // Check if invite is expired
        if (invite.TimeExpires is not null && invite.TimeExpires < DateTime.UtcNow)
            return new TaskResult<ISharedPlanetListInfo>(false, "Invite has expired.");
        
        var planetInfo = await _planetService.GetPlanetInfoAsync(invite.PlanetId);

        if (planetInfo is null)
        {
            return new TaskResult<ISharedPlanetListInfo>(false, "Planet not found for invite code. It may not be set to Public.");
        }

        return TaskResult<ISharedPlanetListInfo>.FromData(planetInfo);
    }

    private Random random = new();
    private const string inviteChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    public async Task<string> GenerateCodeAsync()
    {
        string code;
        bool exists;

        do
        {
            code = new string(Enumerable.Repeat(inviteChars, 8).Select(s => s[random.Next(s.Length)]).ToArray());
            exists = await _db.PlanetInvites.AnyAsync(x => x.Id == code);
        }
        while (exists);
        return code;
    }
}
