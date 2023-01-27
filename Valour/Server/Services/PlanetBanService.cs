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
    private readonly ILogger<PlanetChatChannelService> _logger;

    public PlanetBanService(
        ValourDB db,
        CoreHubService coreHub,
        TokenService tokenService,
        PlanetMemberService memberService,
        ILogger<PlanetChatChannelService> logger)
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
    public async Task<IResult> CreateAsync(PlanetBan ban, PlanetMember member)
    {
        if (ban.IssuerId != member.UserId)
            return Results.BadRequest("IssuerId should match user Id.");

        if (ban.TargetId == member.Id)
            return Results.BadRequest("You cannot ban yourself.");

        // Ensure it doesn't already exist
        if (await db.PlanetBans.AnyAsync(x => x.PlanetId == ban.PlanetId && x.TargetId == ban.TargetId))
            return Results.BadRequest("Ban already exists for user.");

        // Ensure user has more authority than the user being banned
        var target = await _memberService.GetByUserAsync(ban.TargetId, ban.PlanetId);

        if (target is null)
            return ValourResult.NotFound<PlanetMember>();

        if (await _memberService.GetAuthorityAsync(target) >= await _memberService.GetAuthorityAsync(member))
            return ValourResult.Forbid("The target has a higher authority than you.");

        await using var tran = await _db.Database.BeginTransactionAsync();

        try
        {
            ban.Id = IdManager.Generate();

            // Add ban
            await _db.PlanetBans.AddAsync(ban.ToDatabase());

            // Save changes
            await _db.SaveChangesAsync();

            // Delete target member
            await _memberService.DeleteAsync(target);

            // Save changes
            await _db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            _logger.LogError(e.Message);
            await tran.RollbackAsync();
            return Results.Problem(e.Message);
        }

        await tran.CommitAsync();

        // Notify of changes
        _coreHub.NotifyPlanetItemChange(ban);
        _coreHub.NotifyPlanetItemDelete(target);

        return Results.Created($"api/planetbans/{ban.Id}", ban);
    }

    public async Task<IResult> PutAsync(PlanetBan old, PlanetBan updatedban)
    {
        if (updatedban.PlanetId != old.PlanetId)
            return Results.BadRequest("You cannot change the PlanetId.");

        if (updatedban.TargetId != old.TargetId)
            return Results.BadRequest("You cannot change who was banned.");

        if (updatedban.IssuerId != old.IssuerId)
            return Results.BadRequest("You cannot change who banned the user.");

        if (updatedban.TimeCreated != old.TimeCreated)
            return Results.BadRequest("You cannot change the creation time");

        try
        {
            _db.Entry(old).State = EntityState.Detached;
            _db.PlanetBans.Update(updatedban.ToDatabase());
            await _db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            _logger.LogError(e.Message);
            return Results.Problem(e.Message);
        }

        // Notify of changes
        _coreHub.NotifyPlanetItemChange(updatedban);

        return Results.Ok(updatedban);
    }

    /// <summary>
    /// Soft deletes the PlanetMember (and member roles)
    /// </summary>
    public async Task DeleteAsync(PlanetMember member)
    {
        // Remove roles
        var roles = _db.PlanetRoleMembers.Where(x => x.MemberId == member.Id);
        _db.PlanetRoleMembers.RemoveRange(roles);

        // Convert to db 
        var dbMember = member.ToDatabase();
        
        // Soft delete member
        dbMember.IsDeleted = true;
        
        _db.PlanetMembers.Update(dbMember);
        await _db.SaveChangesAsync();
    }
}