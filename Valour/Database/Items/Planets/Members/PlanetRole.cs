using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Valour.Shared;
using Valour.Database.Items.Planets.Channels;
using Valour.Shared.Authorization;
using Valour.Shared.Items.Planets.Members;
using Valour.Shared.Items;
using System.Drawing;
using Valour.Database.Items.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */


namespace Valour.Database.Items.Planets.Members;

public class PlanetRole : PlanetItem, ISharedPlanetRole
{
    [InverseProperty("Role")]
    [JsonIgnore]
    public virtual ICollection<PermissionsNode> PermissionNodes { get; set; }

    /// <summary>
    /// The position of the role: Lower has more authority
    /// </summary>
    public uint Position { get; set; }

    /// <summary>
    /// The planet permissions for the role
    /// </summary>
    public ulong Permissions { get; set; }

    // RGB Components for role color
    public byte Color_Red { get; set; }
    public byte Color_Green { get; set; }
    public byte Color_Blue { get; set; }

    // Formatting options
    public bool Bold { get; set; }
    public bool Italics { get; set; }

    public string Name { get; set; }

    public override ItemType ItemType => ItemType.PlanetRole;

    public uint GetAuthority() =>
        ISharedPlanetRole.GetAuthority(this);

    public Color GetColor() =>
        ISharedPlanetRole.GetColor(this);

    public string GetColorHex() =>
        ISharedPlanetRole.GetColorHex(this);

    public bool HasPermission(PlanetPermission perm) =>
        ISharedPlanetRole.HasPermission(this, perm);

    public ICollection<PermissionsNode> GetNodes(ValourDB db)
    {
        PermissionNodes ??= db.PermissionsNodes.Where(x => x.Role_Id == Id).ToList();
        return PermissionNodes;
    }

    public ICollection<PermissionsNode> GetChannelNodes(ValourDB db)
    {
        PermissionNodes ??= db.PermissionsNodes.Where(x => x.Role_Id == Id).ToList();
        return PermissionNodes.Where(x => x.Target_Type == ItemType.PlanetChatChannel).ToList();
    }

    public ICollection<PermissionsNode> GetCategoryNodes(ValourDB db)
    {
        PermissionNodes ??= db.PermissionsNodes.Where(x => x.Role_Id == Id).ToList();
        return PermissionNodes.Where(x => x.Target_Type == ItemType.PlanetCategoryChannel).ToList();
    }

    public async Task<PermissionsNode> GetChannelNodeAsync(PlanetChatChannel channel, ValourDB db) =>
        await db.PermissionsNodes.FirstOrDefaultAsync(x => x.Target_Id == channel.Id &&
                                                                     x.Target_Type == ItemType.PlanetChatChannel);

    public async Task<PermissionsNode> GetChannelNodeAsync(PlanetCategoryChannel category, ValourDB db) =>
        await db.PermissionsNodes.FirstOrDefaultAsync(x => x.Target_Id == category.Id &&
                                                                     x.Target_Type == ItemType.PlanetChatChannel);

    public async Task<PermissionsNode> GetCategoryNodeAsync(PlanetCategoryChannel category, ValourDB db) =>
        await db.PermissionsNodes.FirstOrDefaultAsync(x => x.Target_Id == category.Id &&
                                                                     x.Target_Type == ItemType.PlanetCategoryChannel);

    public async Task<PermissionState> GetPermissionStateAsync(Permission permission, PlanetChatChannel channel, ValourDB db) =>
        await GetPermissionStateAsync(permission, channel.Id, db);

    public async Task<PermissionState> GetPermissionStateAsync(Permission permission, ulong channel_id, ValourDB db) =>
        (await db.PermissionsNodes.FirstOrDefaultAsync(x => x.Role_Id == Id && x.Target_Id == channel_id)).GetPermissionState(permission);

    public override async Task DeleteAsync(ValourDB db)
    {
        // Remove all members
        var members = db.PlanetRoleMembers.Where(x => x.Role_Id == Id);
        db.PlanetRoleMembers.RemoveRange(members);

        // Remove role nodes
        var nodes = GetNodes(db);

        db.PermissionsNodes.RemoveRange(nodes);

        // Remove self
        db.PlanetRoles.Remove(this);

        await db.SaveChangesAsync();

        // Notify clients
        PlanetHub.NotifyPlanetItemDelete(this);
    }

    public override async Task<TaskResult> CanDeleteAsync(AuthToken token, PlanetMember member, ValourDB db)
        => await CanCreateAsync(token, member, db);

    public override async Task<TaskResult> CanUpdateAsync(AuthToken token, PlanetMember member, PlanetItem old, ValourDB db)
    { 
        var canCreate = await CanCreateAsync(token, member, db);
        if (!canCreate.Success)
            return canCreate;

        var oldRole = old as PlanetRole;

        if (oldRole.Position != Position)
        {
            return new TaskResult(false, "Position cannot be changed directly.");
        }

        return TaskResult.SuccessResult;
    }

    public override async Task<TaskResult> CanCreateAsync(AuthToken token, PlanetMember member, ValourDB db)
    {
        // Needs to be able to GET in order to do anything else
        var canGet = await CanGetAsync(token, member, db);
        if (!canGet.Success)
            return canGet;

        if (!await member.HasPermissionAsync(PlanetPermissions.ManageRoles, db))
            return new TaskResult(false, "Member lacks planet permission " + PlanetPermissions.ManageRoles.Name);

        if (GetAuthority() > await member.GetAuthorityAsync())
            return new TaskResult(false, "You cannot manage roles with higher authority than your own.");

        if (Permissions == PlanetPermissions.FullControl.Value
            && (!await member.HasPermissionAsync(PlanetPermissions.FullControl, db)))
            return new TaskResult(false, "Only an admin can manage admin roles.");

        Position = (uint)await db.PlanetRoles.CountAsync(x => x.Planet_Id == Planet_Id);

        return TaskResult.SuccessResult;
    }

    public override void RegisterCustomRoutes(WebApplication app)
    {
        app.MapGet($"planet/{{planet_id}}/planetmember/{{member_id}}/roles", GetAllRolesForMember);
        app.MapPost($"planet/{{planet_id}}/planetmember/{{member_id}}/roles/{{role_id}}", AddRoleToMember);
        app.MapPut($"planet/{{planet_id}}/planetmember/{{member_id}}/roles/{{role_id}}", AddRoleToMember);
        app.MapDelete($"planet/{{planet_id}}/planetmember/{{member_id}}/roles/{{role_id}}", RemoveRoleFromMember);
    }

    public async Task<IResult> GetAllRolesForMember(ValourDB db, ulong member_id, ulong planet_id, [FromHeader] string authorization)
    {
        var auth = await AuthToken.TryAuthorize(authorization, db);
        if (auth is null)
            return Results.Unauthorized();

        var authMember = await PlanetMember.FindAsync(auth.User_Id, planet_id, db);
        if (authMember is null)
            return Results.Forbid();

        var member = await db.PlanetMembers.Include(x => x.RoleMembership.OrderBy(x => x.Role.Position))
                                           .ThenInclude(x => x.Role)
                                           .FirstOrDefaultAsync(x => x.Id == member_id);
        if (member is null)
            return Results.NotFound();

        if (member.Planet_Id != planet_id)
            return Results.NotFound();

        return Results.Json(member.RoleMembership.Select(r => r.Role_Id));
    }

    public async Task<IResult> AddRoleToMember(ValourDB db, ulong member_id, ulong planet_id, ulong role_id, [FromHeader] string authorization)
    {
        var auth = await AuthToken.TryAuthorize(authorization, db);
        if (auth is null)
            return Results.Unauthorized();

        var authMember = await PlanetMember.FindAsync(auth.User_Id, planet_id, db);
        if (authMember is null)
            return Results.Forbid();

        var member = await db.PlanetMembers.FirstOrDefaultAsync(x => x.Id == member_id);
        if (member is null)
            return Results.NotFound();

        if (member.Planet_Id != planet_id)
            return Results.NotFound();

        if (!await authMember.HasPermissionAsync(PlanetPermissions.ManageRoles, db))
            return Results.Forbid();

        if (await db.PlanetRoleMembers.AllAsync(x => x.Member_Id == member.Id && x.Role_Id == role_id))
            return Results.BadRequest("The member already has this role");

        var role = await db.PlanetRoles.FindAsync(role_id);
        if (role is null)
            return Results.NotFound();

        var authAuthority = await authMember.GetAuthorityAsync();
        if (role.GetAuthority() > authAuthority)
            return Results.Forbid();

        PlanetRoleMember newRoleMember = new()
        {
            Id = IdManager.Generate(),
            Member_Id = member.Id,
            Role_Id = role_id,
            User_Id = member.User_Id
        };

        await db.PlanetRoleMembers.AddAsync(newRoleMember);
        await db.SaveChangesAsync();

        return Results.Created($"{UriPrefix}{Node}{UriPostfix}/planet/{planet_id}/planetrolemember/{newRoleMember.Id}", newRoleMember);
    }

    public async Task<IResult> RemoveRoleFromMember(ValourDB db, ulong member_id, ulong planet_id, ulong role_id, [FromHeader] string authorization)
    {
        var auth = await AuthToken.TryAuthorize(authorization, db);
        if (auth is null)
            return Results.Unauthorized();

        var authMember = await PlanetMember.FindAsync(auth.User_Id, planet_id, db);
        if (authMember is null)
            return Results.Forbid();

        var member = await db.PlanetMembers.FirstOrDefaultAsync(x => x.Id == member_id);
        if (member is null)
            return Results.NotFound();

        if (member.Planet_Id != planet_id)
            return Results.NotFound();

        if (!await authMember.HasPermissionAsync(PlanetPermissions.ManageRoles, db))
            return Results.Forbid();

        var oldRoleMember = await db.PlanetRoleMembers.FirstOrDefaultAsync(x => x.Member_Id == member.Id && x.Role_Id == role_id);

        if (oldRoleMember is null)
            return Results.BadRequest("The member does not have this role");

        var role = await db.PlanetRoles.FindAsync(role_id);
        if (role is null)
            return Results.NotFound();

        var authAuthority = await authMember.GetAuthorityAsync();
        if (role.GetAuthority() > authAuthority)
            return Results.Forbid();

        db.PlanetRoleMembers.Remove(oldRoleMember);
        await db.SaveChangesAsync();

        return Results.NoContent();
    }
}

