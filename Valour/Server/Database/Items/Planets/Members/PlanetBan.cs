using Microsoft.AspNetCore.Mvc;
using Valour.Api.Items.Planets;
using Valour.Shared.Authorization;
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
[Table("planet_bans")]
public class PlanetBan : Item, IPlanetItem, ISharedPlanetBan
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
        $"api/planet/{{planetId}}/{nameof(PlanetBan)}";

    #endregion

    /// <summary>
    /// The member that banned the user
    /// </summary>
    [Column("issuer_id")]
    public long IssuerId { get; set; }

    /// <summary>
    /// The userId of the target that was banned
    /// </summary>
    [Column("target_id")]
    public long TargetId { get; set; }

    /// <summary>
    /// The reason for the ban
    /// </summary>
    [Column("reason")]
    public string Reason { get; set; }

    /// <summary>
    /// The time the ban was placed
    /// </summary>
    [Column("time_created")]
    public DateTime TimeCreated { get; set; }

    /// <summary>
    /// The time the ban expires. Null for permanent.
    /// </summary>
    [Column("time_expires")]
    public DateTime? TimeExpires { get; set; }

    /// <summary>
    /// True if the ban never expires
    /// </summary>
    [NotMapped]
    public bool Permanent => TimeExpires == null;

    #region Routes

    [ValourRoute(HttpVerbs.Get), TokenRequired, InjectDb]
    [PlanetMembershipRequired]
    //[PlanetPermsRequired(PlanetPermissionsEnum.Ban)] (There is an exception to this!)
    public static async Task<IResult> GetRoute(HttpContext ctx, long id)
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
    [PlanetMembershipRequired(permissions: PlanetPermissionsEnum.Ban)]
    public static async Task<IResult> PostRoute(HttpContext ctx, long planetId, [FromBody] PlanetBan ban,
        ILogger<PlanetBan> logger)
    {
        var db = ctx.GetDb();
        var member = ctx.GetMember();

        if (ban is null)
            return Results.BadRequest("Include ban in body.");

        if (ban.PlanetId != planetId)
            return Results.BadRequest("PlanetId mismatch.");

        if (ban.IssuerId != member.UserId)
            return Results.BadRequest("IssuerId should match user Id.");

        if (ban.TargetId == member.Id)
            return Results.BadRequest("You cannot ban yourself.");

        // Ensure it doesn't already exist
        if (await db.PlanetBans.AnyAsync(x => x.PlanetId == ban.PlanetId && x.TargetId == ban.TargetId))
            return Results.BadRequest("Ban already exists for user.");

        // Ensure user has more authority than the user being banned
        var target = await PlanetMember.FindAsyncByUser(ban.TargetId, ban.PlanetId, db);

        if (target is null)
            return ValourResult.NotFound<PlanetMember>();

        if (await target.GetAuthorityAsync(db) >= await member.GetAuthorityAsync(db))
            return ValourResult.Forbid("The target has a higher authority than you.");

        using var tran = await db.Database.BeginTransactionAsync();

        try
        {
            ban.Id = IdManager.Generate();

            // Add ban
            await db.PlanetBans.AddAsync(ban);

            // Save changes
            await db.SaveChangesAsync();

            // Delete target member
            await target.DeleteAsync(db);

            // Save changes
            await db.SaveChangesAsync();
        }
        catch (System.Exception e)
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
    [PlanetMembershipRequired(permissions: PlanetPermissionsEnum.Ban)]
    public static async Task<IResult> PutRoute(HttpContext ctx, long id, long planetId, [FromBody] PlanetBan ban,
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

        if (ban.TimeCreated != old.TimeCreated)
            return Results.BadRequest("You cannot change the creation time");

        try
        {
            db.Entry(old).State = EntityState.Detached;
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
    [PlanetMembershipRequired(permissions: PlanetPermissionsEnum.Ban)]
    public static async Task<IResult> DeleteRoute(HttpContext ctx, long id, long planetId,
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
