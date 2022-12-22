using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using Valour.Server.Database.Items.Users;
using Valour.Server.EndpointFilters;
using Valour.Server.EndpointFilters.Attributes;
using Valour.Shared.Authorization;
using Valour.Shared.Items.Authorization;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Server.Database.Items.Authorization;

[Table("oauth_apps")]
public class OauthApp : Item, ISharedOauthApp
{
    [ForeignKey("OwnerId")]
    [JsonIgnore]
    public virtual User Owner { get; set; }

    /// <summary>
    /// The secret key for the app
    /// </summary>
    [Column("secret")]
    public string Secret { get; set; }

    /// <summary>
    /// The ID of the user that created this app
    /// </summary>
    [Column("owner_id")]
    public long OwnerId { get; set; }

    /// <summary>
    /// The amount of times this app has been used
    /// </summary>
    [Column("uses")]
    public int Uses { get; set; }

    /// <summary>
    /// The image used to represent the app
    /// </summary>
    [Column("image_url")]
    public string ImageUrl { get; set; }

    /// <summary>
    /// The name of the app
    /// </summary>
    [Column("name")]
    public string Name { get; set; }

    /// <summary>
    /// The redirect url for authorization
    /// </summary>
    [Column("redirect_url")]
    public string RedirectUrl { get; set; }

    [ValourRoute(HttpVerbs.Put), TokenRequired]
    [UserPermissionsRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> PutRouteAsync(
        [FromBody] OauthApp app, 
        HttpContext ctx,
        ValourDB db,
        ILogger<User> logger)
    {
        var token = ctx.GetToken();

        // Unlike most other entities, we are just copying over a few fields here and
        // ignoring the rest. There are so many things that *should not* be touched by
        // the API it's smarter to just only do what *should*

        if (app.OwnerId != token.UserId)
            return ValourResult.Forbid("You can only change your own applications.");

        var old = await FindAsync<OauthApp>(app.Id, db);

        old.RedirectUrl = app.RedirectUrl;

        try
        {
            db.OauthApps.Update(old);
            await db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            logger.LogError(e.Message);
            return ValourResult.Problem(e.Message);
        }

        return Results.Json(old);
    }
}