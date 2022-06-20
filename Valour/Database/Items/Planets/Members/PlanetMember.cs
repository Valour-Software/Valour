using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations.Schema;
using Valour.Database.Items.Users;
using Valour.Shared.Authorization;
using Valour.Shared.Items.Planets.Members;
using Valour.Shared;
using Valour.Database.Items.Authorization;
using Valour.Database.Items.Planets.Channels;
using Valour.Shared.Items;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Valour.Shared.Http;
using Valour.Database.Attributes;
using System.Web.Mvc;
using Valour.Database.Extensions;
using Microsoft.Extensions.Logging;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Database.Items.Planets.Members;

/// <summary>
/// This class exists to add server funtionality to the PlanetMember
/// class.
/// </summary>
public class PlanetMember : PlanetItem, ISharedPlanetMember
{

    public const int FLAG_UPDATE_ROLES = 0x01;

    // Relational DB stuff
    [ForeignKey("User_Id")]
    [JsonIgnore]
    public virtual User User { get; set; }

    [InverseProperty("Member")]
    [JsonIgnore]
    public virtual ICollection<PlanetRoleMember> RoleMembership { get; set; }

    /// <summary>
    /// The user within the planet
    /// </summary>
    public ulong User_Id { get; set; }

    /// <summary>
    /// The name to be used within the planet
    /// </summary>
    public string Nickname { get; set; }

    /// <summary>
    /// The pfp to be used within the planet
    /// </summary>
    public string Member_Pfp { get; set; }

    public override ItemType ItemType => ItemType.PlanetMember;

    public static async Task<PlanetMember> FindAsync(ulong user_id, ulong planet_id, ValourDB db)
    {
        return await db.PlanetMembers.FirstOrDefaultAsync(x => x.Planet_Id == planet_id &&
                                                                  x.User_Id == user_id);
    }

    /// <summary>
    /// Returns all of the roles for a planet user
    /// </summary>
    public async Task<List<PlanetRole>> GetRolesAsync(ValourDB db)
    {
        List<PlanetRole> roles;

        if (RoleMembership == null)
        {
            await LoadRoleMembershipAsync(db);
        }

        roles = RoleMembership.Select(x => x.Role).ToList();

        return roles;
    }

    /// <summary>
    /// Loads role membership data from database
    /// </summary>
    public async Task LoadRoleMembershipAsync(ValourDB db) =>
        await db.Attach(this).Collection(x => x.RoleMembership)
                                 .Query()
                                 .Include(x => x.Role)
                                 .OrderBy(x => x.Role.Position)
                                 .LoadAsync();

    /// <summary>
    /// Returns the member's primary role
    /// </summary>
    public async Task<PlanetRole> GetPrimaryRoleAsync(ValourDB db = null)
    {
        if (RoleMembership == null)
        {
            await LoadRoleMembershipAsync(db);
        }

        return RoleMembership.FirstOrDefault().Role;
    }

    /// <summary>
    /// Returns if the member has the given permission
    /// </summary>
    public async Task<bool> HasPermissionAsync(PlanetPermission permission, ValourDB db)
    {
        await GetPlanetAsync(db);
        return await Planet.HasPermissionAsync(this, permission, db);
    }

    /// <summary>
    /// Returns a success or failure for the combination of permissions
    /// Send in pairs of permissions with their target object! For example (ChannelPermission, Channel) or (UserPermission, AuthToken)
    /// 
    /// Todo: Use interface to make this better? IPermissable?
    /// </summary>
    public async Task<TaskResult> HasAllPermissions(ValourDB db, params (Permission perm, object target)[] permission_pairs)
    {
        foreach (var pair in permission_pairs)
        {
            if (pair.perm is UserPermission)
            {
                var uperm = pair.perm as UserPermission;
                var token = pair.target as AuthToken;

                if (!token.HasScope(uperm))
                    return new TaskResult(false, "Token lacks " + uperm.Name + " permission.");
            }
            else if (pair.perm is PlanetPermission)
            {
                var pperm = pair.perm as PlanetPermission;
                var planet = pair.target as Planet;

                if (!await planet.HasPermissionAsync(this, pperm, db))
                    return new TaskResult(false, "Member lacks " + pperm.Name + " planet permission.");
            }
            else if (pair.perm is ChatChannelPermission)
            {
                var cperm = pair.perm as ChatChannelPermission;
                var channel = pair.target as PlanetChatChannel;

                if (!await channel.HasPermissionAsync(this, cperm, db))
                    return new TaskResult(false, "Member lacks " + cperm.Name + " channel permission.");
            }
            else if (pair.perm is CategoryPermission)
            {
                var cperm = pair.perm as CategoryPermission;
                var channel = pair.target as PlanetCategoryChannel;

                if (!await channel.HasPermissionAsync(this, cperm, db))
                    return new TaskResult(false, "Member lacks " + cperm.Name + " category permission.");
            }
            else
            {
                throw new Exception("This type of permission needs to be implemented!");
            }
        }

        return new TaskResult(true, "Authorized.");
    }

    /// <summary>
    /// Returns the user (async)
    /// </summary>
    public async Task<User> GetUserAsync(ValourDB db) => 
        User ??= await db.Users.FindAsync(User_Id);

    public async Task<ulong> GetAuthorityAsync(ValourDB db)
    {
        if (Planet is null)
            await GetPlanetAsync(db);

        if (Planet.Owner_Id == User_Id)
        {
            // Highest possible authority for owner
            return ulong.MaxValue;
        }
        else
        {
            var primaryRole = await GetPrimaryRoleAsync();
            return primaryRole.GetAuthority();
        }
    }

    public async Task DeleteAsync(ValourDB db)
    {
        await db.BulkDeleteAsync(
            db.PlanetRoleMembers.Where(x => x.Member_Id == Id)
        );

        db.PlanetMembers.Remove(this);

        await db.SaveChangesAsync();
    }

    // Helpful route to return the member for the authorizing user
    [ValourRoute(HttpVerbs.Get, "/self"), TokenRequired, InjectDB]
    [PlanetMembershipRequired]
    public static void GetSelfRoute(HttpContext ctx) =>
        Results.Json(ctx.GetMember());

    [ValourRoute(HttpVerbs.Get), TokenRequired, InjectDB]
    [PlanetMembershipRequired]
    public static async Task<IResult> GetRoute(HttpContext ctx, ulong id)
    {
        var db = ctx.GetDB();
        var member = await FindAsync<PlanetMember>(id, db);

        if (member is null)
            return ValourResult.NotFound<PlanetMember>();

        return Results.Json(member);
    }

    [ValourRoute(HttpVerbs.Get, "/byuser/{user_id}"), TokenRequired, InjectDB]
    [PlanetMembershipRequired]
    public static async Task<IResult> GetRoute(HttpContext ctx, ulong planet_id, ulong user_id)
    {
        var db = ctx.GetDB();
        var member = await FindAsync(user_id, planet_id, db);

        if (member is null)
            return ValourResult.NotFound<PlanetMember>();

        return Results.Json(member);
    }

    [ValourRoute(HttpVerbs.Post), TokenRequired, InjectDB]
    public static async Task<IResult> PostRouteAsync(HttpContext ctx, ulong planet_id, string invite_code, [FromBody] PlanetMember member,
        ILogger<PlanetMember> logger)
    {
        var db = ctx.GetDB();
        var token = ctx.GetToken();

        if (member.Planet_Id != planet_id)
            return Results.BadRequest("Planet_Id does not match.");
        if (member.User_Id != token.User_Id)
            return Results.BadRequest("User_Id does not match.");

        var nameValid = ValidateName(member);
        if (!nameValid.Success)
            return Results.BadRequest(nameValid.Message);

        // Clear out pfp, it *must* be done through VMPS
        member.Member_Pfp = null;

        // Ensure member does not already exist
        if (await db.PlanetMembers.AnyAsync(x => x.Planet_Id == planet_id && x.User_Id == token.User_Id))
            return Results.BadRequest("Planet member already exists.");

        var planet = await FindAsync<Planet>(planet_id, db);

        if (!planet.Public)
        {
            if (invite_code is null)
                return ValourResult.Forbid("The planet is not public. Please include invite_code.");

            if (!await db.PlanetInvites.AnyAsync(x => x.Code == invite_code && x.Planet_Id == planet_id && DateTime.UtcNow > x.Created))
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

        PlanetHub.NotifyPlanetItemChange(member);

        return Results.Created(member.GetUri(), member);
    }

    [ValourRoute(HttpVerbs.Put), TokenRequired, InjectDB]
    [PlanetMembershipRequired]
    public static async Task<IResult> PutRouteAsync(HttpContext ctx, ulong id, ulong planet_id, [FromBody] PlanetMember member,
        ILogger<PlanetMember> logger)
    {
        var db = ctx.GetDB();
        var token = ctx.GetToken();

        var old = await FindAsync<PlanetMember>(id, db);

        if (old is null)
            return ValourResult.NotFound<PlanetMember>();

        if (old.Id != member.Id)
            return Results.BadRequest("Cannot change Id.");

        if (token.User_Id != member.User_Id)
            return Results.BadRequest("You can only modify your own membership.");

        if (member.Planet_Id != planet_id)
            return Results.BadRequest("Cannot change Planet_Id.");

        if (old.Member_Pfp != member.Member_Pfp)
            return Results.BadRequest("Cannot directly change pfp. Use VMPS.");

        var nameValid = ValidateName(member);
        if (!nameValid.Success)
            return Results.BadRequest(nameValid.Message);

        try
        {
            db.PlanetMembers.Update(member);
            await db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            logger.LogError(e.Message);
            return Results.Problem(e.Message);
        }

        return Results.Ok(member);
    }

    [ValourRoute(HttpVerbs.Delete), TokenRequired, InjectDB]
    [PlanetMembershipRequired]
    public static async Task<IResult> DeleteRouteAsync(HttpContext ctx, ulong id,
        ILogger<PlanetMember> logger)
    {
        var db = ctx.GetDB();
        var authMember = ctx.GetMember();
        var targetMember = await FindAsync<PlanetMember>(id, db);

        if (authMember.Id != id)
        {
            if (!await authMember.HasPermissionAsync(PlanetPermissions.Kick, db))
                return ValourResult.LacksPermission(PlanetPermissions.Kick);

            if (await authMember.GetAuthorityAsync(db) < await targetMember.GetAuthorityAsync(db))
                return ValourResult.Forbid("You have less authority than the target member.");
        }
            

        var tran = await db.Database.BeginTransactionAsync();

        try
        {
            await targetMember.DeleteAsync(db);
        }
        catch(System.Exception e)
        {
            logger.LogError(e.Message);
            return Results.Problem(e.Message);
        }

        await tran.CommitAsync();
        PlanetHub.NotifyPlanetItemDelete(targetMember);

        return Results.NoContent();
    }

    public static TaskResult ValidateName(PlanetMember member)
    {
        // Ensure nickname is valid
        if (member.Nickname.Length > 32)
        {
            return new TaskResult(false, "Maximum nickname is 32 characters.");
        }

        return TaskResult.SuccessResult;
    }

    [ValourRoute(HttpVerbs.Get, "/{id}/roles"), TokenRequired, InjectDB]
    [PlanetMembershipRequired]
    public async Task<IResult> GetAllRolesForMember(HttpContext ctx, ulong id, ulong planet_id)
    {
        var db = ctx.GetDB();

        var member = await db.PlanetMembers.Include(x => x.RoleMembership.OrderBy(x => x.Role.Position))
                                           .ThenInclude(x => x.Role)
                                           .FirstOrDefaultAsync(x => x.Id == id && x.Planet_Id == planet_id);
        if (member is null)
            return ValourResult.NotFound<PlanetMember>();

        return Results.Json(member.RoleMembership.Select(r => r.Role_Id));
    }

    [ValourRoute(HttpVerbs.Post, "/{id}/roles/{role_id}"), TokenRequired, InjectDB]
    [PlanetMembershipRequired]
    public async Task<IResult> AddRoleToMember(HttpContext ctx, ulong id, ulong planet_id, ulong role_id,
        ILogger<PlanetMember> logger)
    {
        var db = ctx.GetDB();
        var authMember = ctx.GetMember();

        var member = await FindAsync<PlanetMember>(id, db);
        if (member is null)
            return ValourResult.NotFound<PlanetMember>();

        if (member.Planet_Id != planet_id)
            return ValourResult.NotFound<PlanetMember>();

        if (!await authMember.HasPermissionAsync(PlanetPermissions.ManageRoles, db))
            return ValourResult.LacksPermission(PlanetPermissions.ManageRoles);

        if (await db.PlanetRoleMembers.AnyAsync(x => x.Member_Id == member.Id && x.Role_Id == role_id))
            return Results.BadRequest("The member already has this role");

        var role = await db.PlanetRoles.FindAsync(role_id);
        if (role is null)
            return ValourResult.NotFound<PlanetRole>();

        var authAuthority = await authMember.GetAuthorityAsync(db);
        if (role.GetAuthority() > authAuthority)
            return ValourResult.Forbid("You have lower authority than the role you are trying to add");

        PlanetRoleMember newRoleMember = new()
        {
            Id = IdManager.Generate(),
            Member_Id = member.Id,
            Role_Id = role_id,
            User_Id = member.User_Id
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

        PlanetHub.NotifyPlanetItemChange(newRoleMember);

        return Results.Created($"{UriPrefix}{Node}{UriPostfix}/planet/{planet_id}/planetrolemember/{newRoleMember.Id}", newRoleMember);
    }


    [ValourRoute(HttpVerbs.Delete, "/{id}/roles/{role_id}"), TokenRequired, InjectDB]
    [PlanetMembershipRequired]
    public async Task<IResult> RemoveRoleFromMember(HttpContext ctx, ulong id, ulong planet_id, ulong role_id, [FromHeader] string authorization,
        ILogger<PlanetMember> logger)
    {

        var db = ctx.GetDB();
        var authMember = ctx.GetMember();

        var member = await FindAsync<PlanetMember>(id, db);
        if (member is null)
            return ValourResult.NotFound<PlanetMember>();

        if (member.Planet_Id != planet_id)
            return ValourResult.NotFound<PlanetMember>();

        if (!await authMember.HasPermissionAsync(PlanetPermissions.ManageRoles, db))
            return ValourResult.LacksPermission(PlanetPermissions.ManageRoles);

        var oldRoleMember = await db.PlanetRoleMembers.FirstOrDefaultAsync(x => x.Member_Id == member.Id && x.Role_Id == role_id);

        if (oldRoleMember is null)
            return Results.BadRequest("The member does not have this role");

        var role = await db.PlanetRoles.FindAsync(role_id);
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

        PlanetHub.NotifyPlanetItemDelete(oldRoleMember);

        return Results.NoContent();
    }
}

