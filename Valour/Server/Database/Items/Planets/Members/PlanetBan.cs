using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Web.Mvc;
using Valour.Server.Attributes;
using Valour.Server.Database.Extensions;
using Valour.Shared.Authorization;
using Valour.Shared.Http;
using Valour.Shared.Items;
using Valour.Shared.Items.Planets.Members;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Server.Database.Items.Planets.Members;

/// <summary>
/// This represents a user within a planet and is used to represent membership
/// </summary>
public class PlanetBan : PlanetItem, ISharedPlanetBan
{
    /// <summary>
    /// The member that banned the user
    /// </summary>
    public ulong IssuerId { get; set; }

    /// <summary>
    /// The userId of the target that was banned
    /// </summary>
    public ulong TargetId { get; set; }

    /// <summary>
    /// The reason for the ban
    /// </summary>
    public string Reason { get; set; }

    /// <summary>
    /// The time the ban was placed
    /// </summary>
    public DateTime Time { get; set; }

    /// <summary>
    /// The time the ban expires. Null for permanent.
    /// </summary>
    public DateTime? Expires { get; set; }

    /// <summary>
    /// The type of this item
    /// </summary>
    public override ItemType ItemType => ItemType.PlanetBan;

    /// <summary>
    /// True if the ban never expires
    /// </summary>
    public bool Permanent => Expires == null;

    #region Routes

    [ValourRoute(HttpVerbs.Get), TokenRequired, InjectDb]
    [PlanetMembershipRequired]
    //[PlanetPermsRequired(PlanetPermissionsEnum.Ban)] (There is an exception to this!)
    public static async Task<IResult> GetRoute(HttpContext ctx, ulong id)
    {
        var db = ctx.GetDb();
        var ban = await FindAsync<PlanetBan>(id, db);
        var member = ctx.GetMember();

        if (ban is null)
            return ValourResult.NotFound<PlanetBan>();

        // You can retrieve your own ban
        if (ban.TargetId != member.Id)
        {
            if (!await member.HasPermissionAsync(PlanetPermissions.Ban, db))
                return ValourResult.LacksPermission(PlanetPermissions.Ban);
        }

        return Results.Json(ban);
    }

    [ValourRoute(HttpVerbs.Post), TokenRequired, InjectDb]
    [PlanetMembershipRequired, PlanetPermsRequired(PlanetPermissionsEnum.Ban)]
    public static async Task<IResult> PostRoute(HttpContext ctx, ulong planetId, [FromBody] PlanetBan ban,
        ILogger<PlanetBan> logger)
    {
        var db = ctx.GetDb();
        var member = ctx.GetMember();

        if (ban is null)
            return Results.BadRequest("Include ban in body.");

        if (ban.PlanetId != planetId)
            return Results.BadRequest("PlanetId mismatch.");

        if (ban.IssuerId != member.Id)
            return Results.BadRequest("IssuerId should match member Id.");

        if (ban.TargetId == member.Id)
            return Results.BadRequest("You cannot ban yourself.");

        // Ensure it doesn't already exist
        if (await db.PlanetBans.AnyAsync(x => x.PlanetId == ban.PlanetId && x.TargetId == ban.TargetId))
            return Results.BadRequest("Ban already exists for user.");

        // Ensure user has more authority than the user being banned
        var target = await PlanetMember.FindAsync(ban.TargetId, planetId, db);

        if (target is null)
            return ValourResult.NotFound<PlanetMember>();

        if (await target.GetAuthorityAsync(db) >= await member.GetAuthorityAsync(db))
            return ValourResult.Forbid("The target has a higher authority than you.");

        using var tran = await db.Database.BeginTransactionAsync();

        try
        {
            // Add ban
            await db.PlanetBans.AddAsync(ban);

            // Save changes
            await db.SaveChangesAsync();

            // Delete target member
            await target.DeleteAsync(db);
        }
        catch(System.Exception e)
        {
            logger.LogError(e.Message);
            await tran.RollbackAsync();
            return Results.Problem(e.Message);
        }

        await tran.CommitAsync();

        // Notify of changes
        PlanetHub.NotifyPlanetItemChange(ban);
        PlanetHub.NotifyPlanetItemDelete(target);

        return Results.Created(ban.GetUri(), ban);
    }

    [ValourRoute(HttpVerbs.Put), TokenRequired, InjectDb]
    [PlanetMembershipRequired, PlanetPermsRequired(PlanetPermissionsEnum.Ban)]
    public static async Task<IResult> PutRoute(HttpContext ctx, ulong id, ulong planetId, [FromBody] PlanetBan ban,
        ILogger<PlanetBan> logger)
    {
        var db = ctx.GetDb();
        var member = ctx.GetMember();

        if (ban is null)
            return Results.BadRequest("Include updated ban in body.");

        var old = await FindAsync<PlanetBan>(id, db);

        if (old is null)
            return ValourResult.NotFound<PlanetBan>();

        if (ban.PlanetId != old.PlanetId)
            return Results.BadRequest("You cannot change the PlanetId.");

        if (ban.TargetId != old.TargetId)
            return Results.BadRequest("You cannot change who was banned.");

        if (ban.IssuerId != old.IssuerId)
            return Results.BadRequest("You cannot change who banned the user.");

        if (ban.Time != old.Time)
            return Results.BadRequest("You cannot change the creation time");

        try
        {
            db.PlanetBans.Update(ban);
            await db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            logger.LogError(e.Message);
            return Results.Problem(e.Message);
        }

        // Notify of changes
        PlanetHub.NotifyPlanetItemChange(ban);

        return Results.Ok(ban);
    }

    [ValourRoute(HttpVerbs.Delete), TokenRequired, InjectDb]
    [PlanetMembershipRequired, PlanetPermsRequired(PlanetPermissionsEnum.Ban)]
    public static async Task<IResult> DeleteRoute(HttpContext ctx, ulong id, ulong planetId,
        ILogger<PlanetBan> logger)
    {
        var db = ctx.GetDb();
        var member = ctx.GetMember();

        var ban = await FindAsync<PlanetBan>(id, db);

        // Ensure the user unbanning is either the user that made the ban, or someone
        // with equal or higher authority to them

        if (ban.IssuerId != member.Id)
        {
            var banner = await FindAsync<PlanetMember>(ban.IssuerId, db);

            if (await banner.GetAuthorityAsync(db) > await member.GetAuthorityAsync(db))
                return ValourResult.Forbid("The banner of this user has higher authority than you.");
        }

        try
        {
            db.PlanetBans.Remove(ban);
            await db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            logger.LogError(e.Message);
            return Results.Problem(e.Message);
        }
        

        // Notify of changes
        PlanetHub.NotifyPlanetItemDelete(ban);

        return Results.NoContent();
    }

    #endregion
}
