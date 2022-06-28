using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Valour.Server.Database.Items.Planets;
using Valour.Server.Database.Items.Planets.Channels;
using Valour.Server.Database.Items.Planets.Members;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Http;
using Valour.Shared.Items;
using Valour.Shared.Items.Authorization;

namespace Valour.Server.Database.Items.Authorization;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

public class PermissionsNode : PlanetItem, ISharedPermissionsNode
{

    [ForeignKey("RoleId")]
    [JsonIgnore]
    public virtual PlanetRole Role { get; set; }

    /// <summary>
    /// The permission code that this node has set
    /// </summary>
    public ulong Code { get; set; }

    /// <summary>
    /// A mask used to determine if code bits are disabled
    /// </summary>
    public ulong Mask { get; set; }

    /// <summary>
    /// The role this permissions node belongs to
    /// </summary>
    public ulong RoleId { get; set; }

    /// <summary>
    /// The id of the object this node applies to
    /// </summary>
    public ulong TargetId { get; set; }

    /// <summary>
    /// The type of object this node applies to
    /// </summary>
    public ItemType TargetType { get; set; }

    public override ItemType ItemType => throw new NotImplementedException();

    /// <summary>
    /// Returns the node code for this permission node
    /// </summary>
    public PermissionNodeCode GetNodeCode() =>
        ISharedPermissionsNode.GetNodeCode(this);

    /// <summary>
    /// Returns the permission state for a given permission
    /// </summary>
    public PermissionState GetPermissionState(Permission perm) =>
        ISharedPermissionsNode.GetPermissionState(this, perm);

    /// <summary>
    /// Sets a permission to the given state
    /// </summary>
    public void SetPermission(Permission perm, PermissionState state) =>
        ISharedPermissionsNode.SetPermission(this, perm, state);

    /// <summary>
    /// Returns the target of this permissions node
    /// </summary>

    public async Task<PlanetChannel> GetTargetAsync(ValourDB db)
        => await db.PlanetChannels.FindAsync(TargetId);

    public override void RegisterCustomRoutes(WebApplication app)
    {
        app.MapGet($"permissionsnode/{{targetId}}/{{roleId}}", GetNode);
    }


    public async Task<IResult> GetNode(ValourDB db, ulong targetId, ulong roleId,
        [FromHeader] string authorization)
    {
        var authToken = await AuthToken.TryAuthorize(authorization, db);
        if (authToken is null)
            return ValourResult.NoToken();

        var node = await db.PermissionsNodes.FirstOrDefaultAsync(x => x.TargetId == targetId && x.RoleId == roleId);

        if (node is null)
            return Results.NotFound();


        // Since the  node has no useful information about the planet, it's unnecessary to check for membership
        // or permissions on the member requesting this.
        return Results.Json(node);
    }

    public override async Task<TaskResult> CanDeleteAsync(AuthToken token, PlanetMember member, ValourDB db)
    {
        return await Task.FromResult(
            new TaskResult(false, "You cannot directly delete permission nodes.")
       );
    }

    public override async Task<TaskResult> CanUpdateAsync(AuthToken token, PlanetMember member, PlanetItem old, ValourDB db)
    {
        if (!token.HasScope(UserPermissions.PlanetManagement))
            return new TaskResult(false, "Token lacks Planet Management Permission");

        var oldNode = (PermissionsNode)old;

        if (oldNode.RoleId != RoleId)
            return new TaskResult(false, "Cannot change RoleId");

        if (oldNode.TargetId != TargetId)
            return new TaskResult(false, "Cannot change TargetId");

        if (!await member.HasPermissionAsync(PlanetPermissions.ManageRoles, db))
            return new TaskResult(false, "Member lacks PlanetPermissions.ManageRoles");

        var role = await db.PlanetRoles.FindAsync(RoleId);
        if (role is null)
            return new TaskResult(false, "The Role was not found for this node. This should not happen.");

        if (role.GetAuthority() > await member.GetAuthorityAsync())
            return new TaskResult(false, "The node's role has a higher authority than you.");

        return TaskResult.SuccessResult;
    }

    public override async Task<TaskResult> CanCreateAsync(AuthToken token, PlanetMember member, ValourDB db)
    {
        if (!token.HasScope(UserPermissions.PlanetManagement))
            return new TaskResult(false, "Token lacks Planet Management Permission");

        if (!await member.HasPermissionAsync(PlanetPermissions.ManageRoles, db))
            return new TaskResult(false, "Member lacks PlanetPermissions.ManageRoles");

        var role = await db.PlanetRoles.FindAsync(RoleId);
        if (role is null)
            return new TaskResult(false, "The Role for RoleId was not found.");

        if (role.GetAuthority() > await member.GetAuthorityAsync())
            return new TaskResult(false, "The node's role has a higher authority than you.");

        if (await db.PermissionsNodes.AnyAsync(x => x.RoleId == RoleId && x.TargetId == TargetId))
            return new TaskResult(false, "A node already exists for this role and target.");

        return TaskResult.SuccessResult;
    }
}

