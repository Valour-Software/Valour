using Microsoft.AspNetCore.Mvc;
using Valour.Server.Database.Items.Planets;
using Valour.Server.Database.Items.Planets.Channels;
using Valour.Server.Database.Items.Planets.Members;
using Valour.Shared.Authorization;
using Valour.Shared.Items.Authorization;

namespace Valour.Server.Database.Items.Authorization;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

[Table("permissions_node")]
public class PermissionsNode : PlanetItem, ISharedPermissionsNode
{

    [ForeignKey("RoleId")]
    [JsonIgnore]
    public virtual PlanetRole Role { get; set; }

    /// <summary>
    /// The permission code that this node has set
    /// </summary>
    [Column("code")]
    public long Code { get; set; }

    /// <summary>
    /// A mask used to determine if code bits are disabled
    /// </summary>
    [Column("mask")]
    public long Mask { get; set; }

    /// <summary>
    /// The role this permissions node belongs to
    /// </summary>
    [Column("role_id")]
    public long RoleId { get; set; }

    /// <summary>
    /// The id of the object this node applies to
    /// </summary>
    [Column("target_id")]
    public long TargetId { get; set; }

    /// <summary>
    /// The type of object this node applies to
    /// </summary>
    [Column("target_type")]
    public PermissionsTarget TargetType { get; set; }

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

    [ValourRoute(HttpVerbs.Get), TokenRequired, InjectDb]
    public static async Task<IResult> GetNodeRouteAsync(HttpContext ctx, long id)
    {
        var db = ctx.GetDb();

        var node = await FindAsync<PermissionsNode>(id, db);
        if (node is null)
            return ValourResult.NotFound<PermissionsNode>();

        return Results.Json(node);
    }

    [ValourRoute(HttpVerbs.Get, "/{targetId}/{roleId}"), TokenRequired, InjectDb]
    public static async Task<IResult> GetNodeForTargetRouteAsync(HttpContext ctx, long targetId, long roleId)
    {
        var db = ctx.GetDb();

        var node = await db.PermissionsNodes.FirstOrDefaultAsync(x => x.TargetId == targetId && x.RoleId == roleId);
        if (node is null)
            return ValourResult.NotFound<PermissionsNode>();

        return Results.Json(node);
    }

    [ValourRoute(HttpVerbs.Put), TokenRequired, InjectDb]
    [UserPermissionsRequired(UserPermissionsEnum.PlanetManagement)]
    [PlanetMembershipRequired(permissions: PlanetPermissionsEnum.ManageRoles)]
    public static async Task<IResult> PutRouteAsync(HttpContext ctx, long id, [FromBody] PermissionsNode node,
        ILogger<PermissionsNode> logger)
    {
        var db = ctx.GetDb();
        var member = ctx.GetMember();

        var oldNode = await FindAsync<PermissionsNode>(id, db);
        if (oldNode is null)
            return ValourResult.NotFound<PermissionsNode>();

        if (oldNode.RoleId != node.RoleId)
            return Results.BadRequest("Cannot change RoleId");

        if (oldNode.TargetId != node.TargetId)
            return Results.BadRequest("Cannot change TargetId");

        var role = await FindAsync<PlanetRole>(node.RoleId, db);
        if (role is null)
            return ValourResult.NotFound<PlanetRole>();

        if (role.GetAuthority() > await member.GetAuthorityAsync(db))
            return ValourResult.Forbid("The target node's role has higher authority than you.");

        try
        {
            db.PermissionsNodes.Update(node);
            await db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            logger.LogError(e.Message);
            return Results.Problem(e.Message);
        }

        PlanetHub.NotifyPlanetItemChange(node);

        return Results.Json(node);
    }

    [ValourRoute(HttpVerbs.Post), TokenRequired, InjectDb]
    [UserPermissionsRequired(UserPermissionsEnum.PlanetManagement)]
    [PlanetMembershipRequired(permissions: PlanetPermissionsEnum.ManageRoles)]
    public static async Task<IResult> PostRouteAsync(HttpContext ctx, [FromBody] PermissionsNode node,
        ILogger<PermissionsNode> logger)
    {
        var db = ctx.GetDb();
        var member = ctx.GetMember();

        var role = await FindAsync<PlanetRole>(node.RoleId, db);
        if (role is null)
            return ValourResult.NotFound<PlanetRole>();

        var target = await node.GetTargetAsync(db);
        if (target is null)
            return ValourResult.NotFound<PlanetChannel>();

        if (role.GetAuthority() > await member.GetAuthorityAsync(db))
            return ValourResult.Forbid("The target node's role has higher authority than you.");

        if (await db.PermissionsNodes.AnyAsync(x => x.RoleId == node.RoleId && x.TargetId == node.TargetId))
            return Results.BadRequest("A node already exists for this role and target.");

        try
        {
            await db.PermissionsNodes.AddAsync(node);
            await db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            logger.LogError(e.Message);
            return Results.Problem(e.Message);
        }

        PlanetHub.NotifyPlanetItemChange(node);

        return Results.Created(node.GetUri(), node);
    }
}

