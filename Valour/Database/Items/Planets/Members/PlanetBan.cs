using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using System.Web.Mvc;
using Valour.Database.Attributes;
using Valour.Database.Extensions;
using Valour.Database.Items.Authorization;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Http;
using Valour.Shared.Items;
using Valour.Shared.Items.Planets.Members;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Database.Items.Planets.Members;

/// <summary>
/// This represents a user within a planet and is used to represent membership
/// </summary>
public class PlanetBan : PlanetItem, ISharedPlanetBan
{
    /// <summary>
    /// The member that banned the user
    /// </summary>
    public ulong Banner_Id { get; set; }

    /// <summary>
    /// The user_id of the target that was banned
    /// </summary>
    public ulong Target_Id { get; set; }

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

    [ValourRoute(HttpVerbs.Get), TokenRequired, InjectDB]
    [PlanetMembershipRequired("planet_id")]
    //[PlanetPermsRequired("planet_id", PlanetPermissionsEnum.Ban)] (There is an exception to this!)
    public static async Task<IResult> GetRoute(HttpContext ctx, ulong id)
    {
        var db = ctx.GetDB();
        var ban = await FindAsync<PlanetBan>(id, db);
        var member = ctx.GetMember();

        if (ban is null)
            return ValourResult.NotFound<PlanetBan>();

        // You can retrieve your own ban
        if (ban.Target_Id != member.Id)
        {
            if (!await member.HasPermissionAsync(PlanetPermissions.Ban, db))
                return ValourResult.LacksPermission(PlanetPermissions.Ban);
        }

        return Results.Json(ban);
    }

    [ValourRoute(HttpVerbs.Post), TokenRequired, InjectDB]
    [PlanetMembershipRequired("planet_id")]
    [PlanetPermsRequired("planet_id", PlanetPermissionsEnum.Ban)]
    public static async Task<IResult> PostRoute(HttpContext ctx, ulong planet_id, [FromBody] PlanetBan ban,
        ILogger<PlanetBan> logger)
    {
        var db = ctx.GetDB();
        var member = ctx.GetMember();

        if (ban is null)
            return Results.BadRequest("Include ban in body.");

        if (ban.Planet_Id != planet_id)
            return Results.BadRequest("Planet_Id mismatch.");

        if (ban.Banner_Id != member.Id)
            return Results.BadRequest("Banner_Id should match member Id.");

        if (ban.Target_Id == member.Id)
            return Results.BadRequest("You cannot ban yourself.");

        // Ensure it doesn't already exist
        if (await db.PlanetBans.AnyAsync(x => x.Planet_Id == ban.Planet_Id && x.Target_Id == ban.Target_Id))
            return Results.BadRequest("Ban already exists for user.");

        // Ensure user has more authority than the user being banned
        var target = await PlanetMember.FindAsync(ban.Target_Id, planet_id, db);

        if (target is null)
            return ValourResult.NotFound<PlanetMember>();

        if (await target.GetAuthorityAsync(db) >= await member.GetAuthorityAsync(db))
            return ValourResult.Forbid("The target has a higher authority than you.");

        var tran = await db.Database.BeginTransactionAsync();

        try
        {
            // Add ban
            await db.PlanetBans.AddAsync(ban);

            // Delete target member
            await target.DeleteAsync(db);

            // Save changes
            await db.SaveChangesAsync();
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

    [ValourRoute(HttpVerbs.Put), TokenRequired, InjectDB]
    [PlanetMembershipRequired("planet_id")]
    [PlanetPermsRequired("planet_id", PlanetPermissionsEnum.Ban)]
    public static async Task<IResult> PutRoute(HttpContext ctx, ulong id, ulong planet_id, [FromBody] PlanetBan ban,
        ILogger<PlanetBan> logger)
    {
        var db = ctx.GetDB();
        var member = ctx.GetMember();

        if (ban is null)
            return Results.BadRequest("Include updated ban in body.");

        var old = await FindAsync<PlanetBan>(id, db);

        if (old is null)
            return ValourResult.NotFound<PlanetBan>();

        if (ban.Planet_Id != old.Planet_Id)
            return Results.BadRequest("You cannot change the Planet_Id.");

        if (ban.Target_Id != old.Target_Id)
            return Results.BadRequest("You cannot change who was banned.");

        if (ban.Banner_Id != old.Banner_Id)
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

    [ValourRoute(HttpVerbs.Delete), TokenRequired, InjectDB]
    [PlanetMembershipRequired("planet_id")]
    [PlanetPermsRequired("planet_id", PlanetPermissionsEnum.Ban)]
    public static async Task<IResult> DeleteRoute(HttpContext ctx, ulong id, ulong planet_id,
        ILogger<PlanetBan> logger)
    {
        var db = ctx.GetDB();
        var member = ctx.GetMember();

        var ban = await FindAsync<PlanetBan>(id, db);

        // Ensure the user unbanning is either the user that made the ban, or someone
        // with equal or higher authority to them

        if (ban.Banner_Id != member.Id)
        {
            var banner = await FindAsync<PlanetMember>(ban.Banner_Id, db);

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
