using Microsoft.EntityFrameworkCore.Storage;
using Valour.Server.Database;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Server.Services;

public class PlanetBanService
{
    private readonly ValourDB _db;
    private readonly CoreHubService _coreHub;
    private readonly TokenService _tokenService;
    private readonly PlanetMemberService _memberService;
    private readonly ILogger<PlanetBanService> _logger;

    public PlanetBanService(
        ValourDB db,
        CoreHubService coreHub,
        TokenService tokenService,
        PlanetMemberService memberService,
        ILogger<PlanetBanService> logger)
    {
        _db = db;
        _coreHub = coreHub;
        _tokenService = tokenService;
        _memberService = memberService;
        _logger = logger;
    }
    
    /// <summary>
    /// Returns the Planet ban for the given id
    /// </summary>
    public async Task<PlanetBan> GetAsync(long id) =>
        (await _db.PlanetBans.FindAsync(id)).ToModel();

    /// <summary>
    /// Creates the given planetban
    /// </summary>
    public async Task<TaskResult<PlanetBan>> CreateAsync(PlanetBan ban, PlanetMember member)
    {
        if (ban.IssuerId != member.UserId)
            return new(false, "IssuerId should match user Id.");

        if (ban.TargetId == member.Id)
            return new(false, "You cannot ban yourself.");

        // Ensure it doesn't already exist
        if (await _db.PlanetBans.AnyAsync(x => x.PlanetId == ban.PlanetId && x.TargetId == ban.TargetId))
            return new(false, "Ban already exists for user.");

        var target = await _db.PlanetMembers.FirstOrDefaultAsync(x => x.UserId == ban.TargetId && x.PlanetId == ban.PlanetId);

        await using var tran = await _db.Database.BeginTransactionAsync();

        try
        {
            ban.Id = IdManager.Generate();

            // Add ban
            await _db.PlanetBans.AddAsync(ban.ToDatabase());

            // Save changes
            await _db.SaveChangesAsync();

            // Delete target member
            var memberResult = await _memberService.DeleteAsync(target.Id);
            if (!memberResult.Success)
                throw new Exception("Failed to delete member.");

            // Save changes
            await _db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            _logger.LogError(e.Message);
            await tran.RollbackAsync();
            return new(false, "Failed to create ban. An unexpected error occured.");
        }

        await tran.CommitAsync();

        // Notify of changes
        _coreHub.NotifyPlanetItemChange(ban);
        _coreHub.NotifyPlanetItemDelete(target);

        return new(true, "Success", ban);
    }

    public async Task<TaskResult<PlanetBan>> PutAsync(PlanetBan updatedban)
    {
        var old = await _db.PlanetBans.FindAsync(updatedban.Id);
        if (old is null) return new(false, $"PlanetBan not found");

        if (updatedban.PlanetId != old.PlanetId)
            return new(false, "You cannot change the PlanetId.");

        if (updatedban.TargetId != old.TargetId)
            return new(false, "You cannot change who was banned.");

        if (updatedban.IssuerId != old.IssuerId)
            return new(false, "You cannot change who banned the user.");

        if (updatedban.TimeCreated != old.TimeCreated)
            return new(false, "You cannot change the creation time");

        try
        {
            _db.Entry(old).CurrentValues.SetValues(updatedban);
            await _db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            _logger.LogError(e.Message);
            return new(false, e.Message);
        }

        // Notify of changes
        _coreHub.NotifyPlanetItemChange(updatedban);

        return new(true, "Success", updatedban);
    }

    /// <summary>
    /// Deletes the ban
    /// </summary>
    public async Task<TaskResult> DeleteAsync(PlanetBan ban, PlanetMember member)
    {
        // Ensure the user unbanning is either the user that made the ban, or someone
        // with equal or higher authority to them

        if (ban.IssuerId != member.Id)
        {
            var banner = await _memberService.GetAsync(ban.IssuerId);

            if (await _memberService.GetAuthorityAsync(banner) > await _memberService.GetAuthorityAsync(member))
                return new(false, "The banner of this user has higher authority than you.");
        }

        try
        {
            var _old = await _db.PlanetBans.FindAsync(ban.Id);
            if (_old is null) return new(false, $"PlanetBan not found");
            _db.PlanetBans.Remove(_old);
            await _db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            _logger.LogError(e.Message);
            return new(false, e.Message);
        }


        // Notify of changes
        _coreHub.NotifyPlanetItemDelete(ban);

        return new(true, "Success");
    }
}