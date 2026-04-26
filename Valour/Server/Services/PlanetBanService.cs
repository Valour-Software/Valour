using Valour.Server.Database;
using Valour.Shared;
using Valour.Shared.Models;
using Valour.Shared.Models.Staff;
using Valour.Shared.Queries;

namespace Valour.Server.Services;

public class PlanetBanService
{
    private readonly ValourDb _db;
    private readonly CoreHubService _coreHub;
    private readonly TokenService _tokenService;
    private readonly PlanetMemberService _memberService;
    private readonly ModerationAuditService _moderationAuditService;
    private readonly ILogger<PlanetBanService> _logger;

    public PlanetBanService(
        ValourDb db,
        CoreHubService coreHub,
        TokenService tokenService,
        PlanetMemberService memberService,
        ModerationAuditService moderationAuditService,
        ILogger<PlanetBanService> logger)
    {
        _db = db;
        _coreHub = coreHub;
        _tokenService = tokenService;
        _memberService = memberService;
        _moderationAuditService = moderationAuditService;
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
    public async Task<TaskResult<PlanetBan>> CreateAsync(
        PlanetBan ban,
        PlanetMember member,
        ModerationActionSource source = ModerationActionSource.Manual,
        Guid? triggerId = null)
    {
        if (ban.IssuerId != member.UserId)
            return new(false, "IssuerId should match user Id.");

        if (ban.TargetId == member.UserId)
            return new(false, "You cannot ban yourself.");

        // Check for existing ban
        var existingBan = await _db.PlanetBans.FirstOrDefaultAsync(x => x.PlanetId == ban.PlanetId && x.TargetId == ban.TargetId);
        if (existingBan is not null)
        {
            // If the existing ban is still active, reject
            if (existingBan.TimeExpires == null || existingBan.TimeExpires > DateTime.UtcNow)
                return new(false, "Ban already exists for user.");

            // Expired ban — remove it so we can create a fresh one
            _db.PlanetBans.Remove(existingBan);
            await _db.SaveChangesAsync();
        }

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
            var memberResult = await _memberService.DeleteAsync(target.Id, false); // False because we already have a db transaction
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
        _coreHub.NotifyPlanetItemCreate(ban);
        _coreHub.NotifyPlanetItemDelete(target);

        var actorUserId = source == ModerationActionSource.Automod
            ? ISharedUser.VictorUserId
            : member.UserId;

        await _moderationAuditService.LogAsync(
            ban.PlanetId,
            source,
            ModerationActionType.Ban,
            actorUserId: actorUserId,
            targetUserId: ban.TargetId,
            targetMemberId: target?.Id,
            triggerId: triggerId,
            details: ban.Reason,
            timeCreated: ban.TimeCreated);

        return new(true, "Success", ban);
    }

    public async Task<TaskResult<PlanetBan>> PutAsync(PlanetBan updatedban, long? actorUserId = null)
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

        await _moderationAuditService.LogAsync(
            updatedban.PlanetId,
            ModerationActionSource.Manual,
            ModerationActionType.BanUpdated,
            actorUserId: actorUserId ?? updatedban.IssuerId,
            targetUserId: updatedban.TargetId,
            details: updatedban.Reason);

        return new(true, "Success", updatedban);
    }

    public async Task<QueryResponse<PlanetBan>> QueryPlanetBansAsync(
        long planetId,
        QueryRequest queryRequest)
    {
        var take = queryRequest.Take;
        if (take > 50)
            take = 50;
        
        var skip = queryRequest.Skip;

        var query = _db.PlanetBans
            .AsNoTracking()
            .Where(x => x.PlanetId == planetId)
            .Join(_db.Users.AsNoTracking(), b => b.TargetId, u => u.Id, (b, u) => new { Ban = b, Target = u });

        var search = queryRequest.Options?.Filters?.GetValueOrDefault("search");
        
        if (!string.IsNullOrWhiteSpace(search))
        {
            var lowered = search.ToLower();
            query = query.Where(x => EF.Functions.ILike((x.Target.Name.ToLower() + "#" + x.Target.Tag), $"%{lowered}%") ||
                                      EF.Functions.ILike(x.Target.Name.ToLower(), $"%{lowered}%"));
        }

        var sortField = queryRequest.Options?.Sort?.Field;
        var sortDesc = queryRequest.Options?.Sort?.Descending ?? false;
        query = sortField switch
        {
            "user" => sortDesc
                ? query.OrderByDescending(x => x.Target.Name)
                : query.OrderBy(x => x.Target.Name),
            "created" => sortDesc
                ? query.OrderByDescending(x => x.Ban.TimeCreated)
                : query.OrderBy(x => x.Ban.TimeCreated),
            _ => query.OrderByDescending(x => x.Ban.TimeCreated)
        };

        var total = await query.CountAsync();

        var items = await query
            .Skip(skip)
            .Take(take)
            .Select(x => x.Ban.ToModel())
            .ToListAsync();

        return new QueryResponse<PlanetBan>
        {
            Items = items,
            TotalCount = total
        };
    }

    /// <summary>
    /// Deletes the ban
    /// </summary>
    public async Task<TaskResult> DeleteAsync(PlanetBan ban, PlanetMember member)
    {
        // Ensure the user unbanning is either the user that made the ban, or someone
        // with equal or higher authority to them

        if (ban.IssuerId != member.UserId)
        {
            var banner = await _memberService.GetByUserAsync(ban.IssuerId, ban.PlanetId);

            if (banner is not null && await _memberService.GetAuthorityAsync(banner) > await _memberService.GetAuthorityAsync(member))
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

        await _moderationAuditService.LogAsync(
            ban.PlanetId,
            ModerationActionSource.Manual,
            ModerationActionType.Unban,
            actorUserId: member.UserId,
            targetUserId: ban.TargetId,
            details: ban.Reason);

        return new(true, "Success");
    }
}
