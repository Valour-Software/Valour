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
        var old = await _db.PlanetInvites.FindAsync(updatedInvite.Id);
        if (old is null) return new(false, $"PlanetInvite not found");

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
        try
        {
            var deleted = await _db.PlanetInvites.Where(x => x.Id == invite.Id).ExecuteDeleteAsync();
            if (deleted == 0)
                return new(false, "Invite not found or already deleted.");
        }
        catch (System.Exception e)
        {
            _logger.LogError(e.Message);
            return new(false, e.Message);
        }

        _coreHub.NotifyPlanetItemDelete(invite);

        return TaskResult.SuccessResult;
    }
    
    public async Task<TaskResult<PlanetListInfo>> GetPlanetInfoByInviteCode(string inviteCode)
    {
        var invite = await _db.PlanetInvites.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == inviteCode);
        
        if (invite is null)
            return new TaskResult<PlanetListInfo>(false, "Invite not found.");
        
        // Check if invite is expired
        if (invite.TimeExpires is not null && invite.TimeExpires < DateTime.UtcNow)
            return new TaskResult<PlanetListInfo>(false, "Invite has expired.");
        
        var planetInfo = await _planetService.GetPlanetInfoAsync(invite.PlanetId);

        if (planetInfo is null)
        {
            return new TaskResult<PlanetListInfo>(false, "Planet not found for invite code. It may not be set to Public.");
        }

        return TaskResult<PlanetListInfo>.FromData(planetInfo);
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