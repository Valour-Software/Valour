using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using Valour.Server.Database.Items.Channels.Planets;
using Valour.Server.Database.Items.Planets;
using Valour.Server.Database.Items.Planets.Members;
using Valour.Server.EndpointFilters;
using Valour.Server.EndpointFilters.Attributes;
using Valour.Server.Hubs;
using Valour.Server.Services;
using Valour.Shared.Authorization;
using Valour.Shared.Items.Authorization;

namespace Valour.Database.Items.Authorization;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

[Table("permissions_nodes")]
public class PermissionsNode : Item, IPlanetItem, ISharedPermissionsNode
{
    #region IPlanetItem Implementation

    [JsonIgnore]
    [ForeignKey("PlanetId")]
    public Planet Planet { get; set; }

    [Column("planet_id")]
    public long PlanetId { get; set; }

    public ValueTask<Planet> GetPlanetAsync(ValourDB db) =>
        IPlanetItem.GetPlanetAsync(this, db);

    [JsonIgnore]
    public override string BaseRoute =>
        $"api/planet/{{planetId}}/{nameof(PermissionsNode)}";

    #endregion

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
    public PermissionsTargetType TargetType { get; set; }

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

    [ValourRoute(HttpVerbs.Get), TokenRequired]
    public static async Task<IResult> GetNodeRouteAsync(
        long id, 
        HttpContext ctx, 
        ValourDB db)
    {
        var node = await FindAsync<PermissionsNode>(id, db);
        if (node is null)
            return ValourResult.NotFound<PermissionsNode>();

        return Results.Json(node);
    }

    [ValourRoute(HttpVerbs.Get, "/{type}/{targetId}/{roleId}", $"/api/{nameof(PermissionsNode)}"), TokenRequired]
    public static async Task<IResult> GetNodeForTargetRouteAsync(
        PermissionsTargetType type, 
        long targetId, 
        long roleId, 
        ValourDB db)
    {
        var node = await db.PermissionsNodes.FirstOrDefaultAsync(x => x.TargetId == targetId && x.RoleId == roleId && x.TargetType == type);
        if (node is null)
            return ValourResult.NotFound<PermissionsNode>();

        return Results.Json(node);
    }

    [ValourRoute(HttpVerbs.Put, "/{type}/{targetId}/{roleId}", $"/api/{nameof(PermissionsNode)}"), TokenRequired]
    // Planet permissions are not required in attribute because
    // There will be more permissions than just planet permissions!
    public static async Task<IResult> PutRouteAsync(
        [FromBody] PermissionsNode node,
        PermissionsTargetType type,
        long targetId, 
        long roleId,
        HttpContext ctx,
        ValourDB db,
        CoreHubService hubService,
        ILogger<PermissionsNode> logger)
    {
        var token = ctx.GetToken();

        if (node.TargetId != targetId)
            return Results.BadRequest("TargetId mismatch");
        if (node.RoleId != roleId)
            return Results.BadRequest("RoleId mismatch");
        if (node.TargetType != type)
            return Results.BadRequest("Type mismatch");

        if (!token.HasScope(UserPermissions.PlanetManagement))
            return ValourResult.LacksPermission(UserPermissions.PlanetManagement);

        // Unfortunately we have to do the permissions in here
        var planet = await FindAsync<Planet>(node.PlanetId, db);
        if (planet is null)
            return ValourResult.NotFound<Planet>();

        var member = await PlanetMember.FindAsyncByUser(token.UserId, planet.Id, db);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await planet.HasPermissionAsync(member, PlanetPermissions.ManageRoles, db))
            return ValourResult.LacksPermission(PlanetPermissions.ManageRoles);

        var oldNode = await db.PermissionsNodes.FirstOrDefaultAsync(x => x.TargetId == targetId && x.RoleId == roleId && x.TargetType == type);
        if (oldNode is null)
            return ValourResult.NotFound<PermissionsNode>();

        if (oldNode.RoleId != node.RoleId)
            return Results.BadRequest("Cannot change RoleId");

        if (oldNode.TargetId != node.TargetId)
            return Results.BadRequest("Cannot change TargetId");

        if (oldNode.TargetType != node.TargetType)
            return Results.BadRequest("Cannot change TargetType");

        var role = await FindAsync<PlanetRole>(node.RoleId, db);
        if (role is null)
            return ValourResult.NotFound<PlanetRole>();

        if (role.GetAuthority() > await member.GetAuthorityAsync(db))
            return ValourResult.Forbid("The target node's role has higher authority than you.");
        try
        {
            db.Entry(oldNode).State = EntityState.Detached;
            db.PermissionsNodes.Update(node);
            await db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            logger.LogError(e.Message);
            return Results.Problem(e.Message);
        }

        hubService.NotifyPlanetItemChange(node);

        return Results.Json(node);
    }

    [ValourRoute(HttpVerbs.Post, prefix: $"/api/{nameof(PermissionsNode)}"), TokenRequired]
    // Planet permissions are not required in attribute because
    // There will be more permissions than just planet permissions!
    public static async Task<IResult> PostRouteAsync(
        [FromBody] PermissionsNode node, 
        HttpContext ctx,
        ValourDB db,
        CoreHubService hubService,
        ILogger<PermissionsNode> logger)
    {
        var token = ctx.GetToken();

        if (!token.HasScope(UserPermissions.PlanetManagement))
            return ValourResult.LacksPermission(UserPermissions.PlanetManagement);

        // Unfortunately we have to do the permissions in here
        var planet = await FindAsync<Planet>(node.PlanetId, db);
        if (planet is null)
            return ValourResult.NotFound<Planet>();

        var member = await PlanetMember.FindAsyncByUser(token.UserId, planet.Id, db);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await planet.HasPermissionAsync(member, PlanetPermissions.ManageRoles, db))
            return ValourResult.LacksPermission(PlanetPermissions.ManageRoles);

        var role = await FindAsync<PlanetRole>(node.RoleId, db);
        if (role is null)
            return ValourResult.NotFound<PlanetRole>();

        var target = await node.GetTargetAsync(db);
        if (target is null)
            return ValourResult.NotFound<PlanetChannel>();

        if (target.PermissionsTargetType != node.TargetType)
        {
            // Special case: Categories have a sub-node with PlanetChatChannel type!
            if (!(target.PermissionsTargetType == PermissionsTargetType.PlanetCategoryChannel &&
                node.TargetType == PermissionsTargetType.PlanetChatChannel))
            {
                return Results.BadRequest("TargetType mismatch.");
            }
        }

        if (role.GetAuthority() > await member.GetAuthorityAsync(db))
            return ValourResult.Forbid("The target node's role has higher authority than you.");

        if (await db.PermissionsNodes.AnyAsync(x => x.TargetId == node.TargetId && x.RoleId == node.RoleId && x.TargetType == node.TargetType))
            return Results.BadRequest("A node already exists for this role and target.");

        node.Id = IdManager.Generate();

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

        hubService.NotifyPlanetItemChange(node);

        return Results.Created(node.GetUri(), node);
    }
}

