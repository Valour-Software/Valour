using Microsoft.AspNetCore.Mvc;
using Valour.Server.Database.Items.Authorization;
using Valour.Server.Database.Items.Channels.Planets;
using Valour.Server.Database.Items.Users;
using Valour.Server.EndpointFilters;
using Valour.Server.EndpointFilters.Attributes;
using Valour.Server.Hubs;
using Valour.Server.Services;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Items.Authorization;
using Valour.Shared.Items.Planets.Members;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Server.Database.Items.Planets.Members;

/// <summary>
/// This class exists to add server funtionality to the PlanetMember
/// class.
/// </summary>
[Table("planet_members")]
public class PlanetMember : Item, IPlanetItem, ISharedPlanetMember
{
    #region IPlanetItem Implementation

    [JsonIgnore]
    [ForeignKey("PlanetId")]
    public Planet Planet { get; set; }

    [Column("planet_id")]
    public long PlanetId { get; set; }

    public async ValueTask<Planet> GetPlanetAsync(PlanetService service)
    {
        Planet ??= await service.GetAsync(PlanetId);
        return Planet;
    }

    [JsonIgnore]
    public override string BaseRoute =>
        $"api/planet/{{planetId}}/{nameof(PlanetMember)}";

    #endregion

    public const int FLAG_UPDATE_ROLES = 0x01;

    // Relational DB stuff
    [ForeignKey("UserId")]
    [JsonIgnore]
    public virtual User User { get; set; }

    [InverseProperty("Member")]
    [JsonIgnore]
    public virtual ICollection<PlanetRoleMember> RoleMembership { get; set; }

    /// <summary>
    /// The user within the planet
    /// </summary>
    [Column("user_id")]
    public long UserId { get; set; }

    /// <summary>
    /// The name to be used within the planet
    /// </summary>
    [Column("nickname")]
    public string Nickname { get; set; }

    /// <summary>
    /// The pfp to be used within the planet
    /// </summary>
    [Column("member_pfp")]
    public string MemberPfp { get; set; }

    /// <summary>
    /// Soft-delete flag
    /// </summary>
    [Column("is_deleted")]
    public bool IsDeleted { get; set; }
    
    public async Task<List<PlanetRole>> GetRolesAsync(PlanetMemberService service) =>
        await service.GetRolesAsync(Id);
    
    public async Task<List<PlanetRole>> GetRolesAndNodesAsync(long targetId, PermissionsTargetType type, PlanetMemberService service) =>
        await service.GetRolesAndNodesAsync(Id, targetId, type);
    
    public async Task<PlanetRole> GetPrimaryRoleAsync(PlanetMemberService service) =>
        await service.GetPrimaryRoleAsync(Id);

    /// <summary>
    /// Returns if the member has the given permission
    /// </summary>
    public async Task<bool> HasPermissionAsync(PlanetPermission permission, PermissionsService permService) =>
        await permService.HasPermissionAsync(this, permission);

    /// <summary>
    /// Returns the user (async)
    /// </summary>
    public async Task<User> GetUserAsync(ValourDB db) =>
        User ??= await db.Users.FindAsync(UserId);

    public async Task<int> GetAuthorityAsync(ValourDB db)
    {
        if (Planet is null)
            await GetPlanetAsync(db);

        if (Planet.OwnerId == UserId)
        {
            // Highest possible authority for owner
            return int.MaxValue;
        }
        else
        {
            var primaryRole = await GetPrimaryRoleAsync(db);
            return primaryRole?.GetAuthority() ?? int.MinValue;
        }
    }

    public async Task DeleteAsync(ValourDB db)
    {
        // Remove roles
        var roles = db.PlanetRoleMembers.Where(x => x.MemberId == Id);
        db.PlanetRoleMembers.RemoveRange(roles);

        // Soft delete member
        IsDeleted = true;
        db.PlanetMembers.Update(this);
    }

    // Helpful route to return the member for the authorizing user
    [ValourRoute(HttpVerbs.Get, "/self"), TokenRequired]
    [PlanetMembershipRequired]
    public static void GetSelfRoute(HttpContext ctx) =>
        Results.Json(ctx.GetMember());

    [ValourRoute(HttpVerbs.Get), TokenRequired]
    [PlanetMembershipRequired]
    public static async Task<IResult> GetRouteAsync(long id, ValourDB db)
    {
        var member = await FindAsync<PlanetMember>(id, db);

        if (member is null)
            return ValourResult.NotFound<PlanetMember>();

        return Results.Json(member);
    }

    [ValourRoute(HttpVerbs.Get, "/{id}/authority"), TokenRequired]
    [PlanetMembershipRequired]
    public static async Task<IResult> GetAuthorityRouteAsync(
        long id, 
        ValourDB db)
    {
        var member = await FindAsync<PlanetMember>(id, db);

        if (member is null)
            return ValourResult.NotFound<PlanetMember>();

        return Results.Json(await member.GetAuthorityAsync(db));
    }

    [ValourRoute(HttpVerbs.Get, "/byuser/{userId}"), TokenRequired]
    [PlanetMembershipRequired]
    public static async Task<IResult> GetRoute(
        long planetId, 
        long userId, 
        ValourDB db)
    {
        var member = await FindAsyncByUser(userId, planetId, db);

        if (member is null)
            return ValourResult.NotFound<PlanetMember>();

        return Results.Json(member);
    }

    [ValourRoute(HttpVerbs.Post), TokenRequired]
    public static async Task<IResult> PostRouteAsync(
        [FromBody] PlanetMember member, 
        long planetId, 
        string inviteCode, 
        HttpContext ctx,
        ValourDB db,
        CoreHubService hubService,
        ILogger<PlanetMember> logger)
    {
        var token = ctx.GetToken();

        if (member.PlanetId != planetId)
            return Results.BadRequest("PlanetId does not match.");
        if (member.UserId != token.UserId)
            return Results.BadRequest("UserId does not match.");

        var nameValid = ValidateName(member);
        if (!nameValid.Success)
            return Results.BadRequest(nameValid.Message);

        // Clear out pfp, it *must* be done through VMPS
        member.MemberPfp = null;

        // Ensure member does not already exist
        if (await db.PlanetMembers.AnyAsync(x => x.PlanetId == planetId && x.UserId == token.UserId))
            return Results.BadRequest("Planet member already exists.");

        var planet = await FindAsync<Planet>(planetId, db);

        if (!planet.Public)
        {
            if (inviteCode is null)
                return ValourResult.Forbid("The planet is not public. Please include inviteCode.");

            if (!await db.PlanetInvites.AnyAsync(x => x.Code == inviteCode && x.PlanetId == planetId && DateTime.UtcNow > x.TimeCreated))
                return ValourResult.Forbid("The invite code is invalid or expired.");
        }

        try
        {
            await db.AddAsync(member);
            await db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            logger.LogError(e.Message);
            return Results.Problem(e.Message);
        }

        hubService.NotifyPlanetItemChange(member);

        return Results.Created(member.GetUri(), member);
    }

    [ValourRoute(HttpVerbs.Put), TokenRequired]
    [PlanetMembershipRequired]
    public static async Task<IResult> PutRouteAsync(
        [FromBody] PlanetMember member, 
        long id, 
        long planetId, 
        HttpContext ctx,
        ValourDB db,
        CoreHubService hubService,
        ILogger<PlanetMember> logger)
    {
        var token = ctx.GetToken();

        var old = await FindAsync<PlanetMember>(id, db);

        if (old is null)
            return ValourResult.NotFound<PlanetMember>();

        if (old.Id != member.Id)
            return Results.BadRequest("Cannot change Id.");

        if (token.UserId != member.UserId)
            return Results.BadRequest("You can only modify your own membership.");

        if (member.PlanetId != planetId)
            return Results.BadRequest("Cannot change PlanetId.");

        if (old.MemberPfp != member.MemberPfp)
            return Results.BadRequest("Cannot directly change pfp. Use VMPS.");

        var nameValid = ValidateName(member);
        if (!nameValid.Success)
            return Results.BadRequest(nameValid.Message);

        try
        {
            db.Entry(old).State = EntityState.Detached;
            db.PlanetMembers.Update(member);
            await db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            logger.LogError(e.Message);
            return Results.Problem(e.Message);
        }
        
        hubService.NotifyPlanetItemChange(member);

        return Results.Ok(member);
    }

    [ValourRoute(HttpVerbs.Delete), TokenRequired]
    [PlanetMembershipRequired]
    public static async Task<IResult> DeleteRouteAsync(
        long id, 
        HttpContext ctx,
        ValourDB db,
        CoreHubService hubService,
        ILogger<PlanetMember> logger)
    {
        var authMember = ctx.GetMember();
        var targetMember = await FindAsync<PlanetMember>(id, db);

        if (authMember.Id != id)
        {
            if (!await authMember.HasPermissionAsync(PlanetPermissions.Kick, db))
                return ValourResult.LacksPermission(PlanetPermissions.Kick);

            if (await authMember.GetAuthorityAsync(db) < await targetMember.GetAuthorityAsync(db))
                return ValourResult.Forbid("You have less authority than the target member.");
        }

        await using var tran = await db.Database.BeginTransactionAsync();

        try
        {
            await targetMember.DeleteAsync(db);
            await db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            logger.LogError(e.Message);
            return Results.Problem(e.Message);
        }

        await tran.CommitAsync();
        hubService.NotifyPlanetItemDelete(targetMember);

        return Results.NoContent();
    }

    private static TaskResult ValidateName(PlanetMember member)
    {
        // Ensure nickname is valid
        return member.Nickname.Length > 32 ? new TaskResult(false, "Maximum nickname is 32 characters.") : 
            TaskResult.SuccessResult;
    }

    [ValourRoute(HttpVerbs.Get, "/{id}/roles"), TokenRequired]
    [PlanetMembershipRequired]
    public static async Task<IResult> GetAllRolesForMember(long id, long planetId, ValourDB db)
    {
        var member = await db.PlanetMembers.Include(x => x.RoleMembership.OrderBy(x => x.Role.Position))
                                           .ThenInclude(x => x.Role)
                                           .FirstOrDefaultAsync(x => x.Id == id && x.PlanetId == planetId);
        
        return member is null ? ValourResult.NotFound<PlanetMember>() : 
            Results.Json(member.RoleMembership.Select(r => r.RoleId));
    }

    [ValourRoute(HttpVerbs.Post, "/{id}/roles/{roleId}"), TokenRequired]
    [PlanetMembershipRequired]
    public static async Task<IResult> AddRoleToMember(
        long id, 
        long planetId, 
        long roleId, 
        HttpContext ctx,
        ValourDB db,
        CoreHubService hubService,
        ILogger<PlanetMember> logger)
    {
        var authMember = ctx.GetMember();

        var member = await FindAsync<PlanetMember>(id, db);
        if (member is null)
            return ValourResult.NotFound<PlanetMember>();

        if (member.PlanetId != planetId)
            return ValourResult.NotFound<PlanetMember>();

        if (!await authMember.HasPermissionAsync(PlanetPermissions.ManageRoles, db))
            return ValourResult.LacksPermission(PlanetPermissions.ManageRoles);

        if (await db.PlanetRoleMembers.AnyAsync(x => x.MemberId == member.Id && x.RoleId == roleId))
            return Results.BadRequest("The member already has this role");

        var role = await db.PlanetRoles.FindAsync(roleId);
        if (role is null)
            return ValourResult.NotFound<PlanetRole>();

        var authAuthority = await authMember.GetAuthorityAsync(db);
        if (role.GetAuthority() > authAuthority)
            return ValourResult.Forbid("You have lower authority than the role you are trying to add");

        PlanetRoleMember newRoleMember = new()
        {
            Id = IdManager.Generate(),
            MemberId = member.Id,
            RoleId = roleId,
            UserId = member.UserId,
            PlanetId = member.PlanetId
        };

        try
        {
            await db.PlanetRoleMembers.AddAsync(newRoleMember);
            await db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            logger.LogError(e.Message);
            return Results.Problem(e.Message);
        }

        hubService.NotifyPlanetItemChange(newRoleMember);

        return Results.Created(newRoleMember.GetUri(), newRoleMember);
    }


    [ValourRoute(HttpVerbs.Delete, "/{id}/roles/{roleId}"), TokenRequired]
    [PlanetMembershipRequired]
    public static async Task<IResult> RemoveRoleFromMember(
        long id, 
        long planetId, 
        long roleId, 
        HttpContext ctx,
        ValourDB db,
        CoreHubService hubService,
        ILogger<PlanetMember> logger)
    {
        var authMember = ctx.GetMember();

        var member = await FindAsync<PlanetMember>(id, db);
        if (member is null)
            return ValourResult.NotFound<PlanetMember>();

        if (member.PlanetId != planetId)
            return ValourResult.NotFound<PlanetMember>();

        if (!await authMember.HasPermissionAsync(PlanetPermissions.ManageRoles, db))
            return ValourResult.LacksPermission(PlanetPermissions.ManageRoles);

        var oldRoleMember = await db.PlanetRoleMembers.FirstOrDefaultAsync(x => x.MemberId == member.Id && x.RoleId == roleId);

        if (oldRoleMember is null)
            return Results.BadRequest("The member does not have this role");

        var role = await db.PlanetRoles.FindAsync(roleId);
        if (role is null)
            return ValourResult.NotFound<PlanetRole>();

        var authAuthority = await authMember.GetAuthorityAsync(db);
        if (role.GetAuthority() > authAuthority)
            return ValourResult.Forbid("You have less authority than the role you are trying to remove"); ;

        try
        {
            db.PlanetRoleMembers.Remove(oldRoleMember);
            await db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            logger.LogError(e.Message);
            return Results.Problem(e.Message);
        }

        hubService.NotifyPlanetItemDelete(oldRoleMember);

        return Results.NoContent();
    }
}

