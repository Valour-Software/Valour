using System.Security.Cryptography;
using Valour.Client.Pages;
using Valour.Server.Database;
using Valour.Shared;

namespace Valour.Server.Services;

public class PlanetInviteService
{
    private readonly ValourDB _db;
    private readonly ILogger<PlanetInviteService> _logger;
    private readonly CoreHubService _coreHub;

    public PlanetInviteService(
        ValourDB db,
        ILogger<PlanetInviteService> logger,
        CoreHubService coreHub)
    {
        _db = db;
        _logger = logger;
        _coreHub = coreHub;
    }

    public async Task<PlanetInvite> GetAsync(long id) => 
        (await _db.PlanetInvites.FindAsync(id)).ToModel();

    public async Task<PlanetInvite> GetAsync(string code) =>
        (await _db.PlanetInvites.FirstOrDefaultAsync(x => x.Code == code))
        .ToModel();

    public async Task<PlanetInvite> GetAsync(string code, long planetId) => 
        (await _db.PlanetInvites.FirstOrDefaultAsync(x => x.Code == code 
                                                                 && x.PlanetId == planetId))
        .ToModel();
    
    public async Task<TaskResult<PlanetInvite>> CreateAsync(PlanetInvite invite, PlanetMember issuer)
    {
        invite.Id = IdManager.Generate();
        invite.IssuerId = issuer.UserId;
        invite.TimeCreated = DateTime.UtcNow;
        invite.Code = await GenerateCodeAsync();

        try
        {
            await _db.AddAsync(invite);
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

    public async Task<TaskResult<PlanetInvite>> UpdateAsync(PlanetInvite oldInvite, PlanetInvite updatedInvite)
    {
        if (updatedInvite.Code != oldInvite.Code)
            return new(false, "You cannot change the code.");
        if (updatedInvite.IssuerId != oldInvite.IssuerId)
            return new(false, "You cannot change who issued.");
        if (updatedInvite.TimeCreated != oldInvite.TimeCreated)
            return new(false, "You cannot change the creation time.");
        if (updatedInvite.PlanetId != oldInvite.PlanetId)
            return new(false, "You cannot change what planet.");
        try
        {
            var _old = await _db.PlanetInvites.FindAsync(updatedInvite.Id);
            if (_old is null) return new(false, $"PlanetInvite not found");
            _db.Entry(_old).CurrentValues.SetValues(updatedInvite);
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
            _db.PlanetInvites.Remove(invite.ToDatabase());
            await _db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            _logger.LogError(e.Message);
            return new(false, e.Message);
        }

        _coreHub.NotifyPlanetItemDelete(invite);

        return new(true, "Success");
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
            exists = await _db.PlanetInvites.AnyAsync(x => x.Code == code);
        }
        while (exists);
        return code;
    }
}