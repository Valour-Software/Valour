using Valour.Server.Database.Items.Users;
using Valour.Shared.Authorization;
using Valour.Shared.Items.Channels.Users;

namespace Valour.Server.Database.Items.Channels.Users;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

[Table("direct_chat_channels")]
public class DirectChatChannel : Channel, ISharedDirectChatChannel
{
    /// <summary>
    /// One of the users in the DM channel
    /// </summary>
    [ForeignKey("UserOneId")]
    public virtual User UserOne { get; set; }

    /// <summary>
    /// One of the users in the DM channel
    /// </summary>
    [ForeignKey("UserTwoId")]
    public virtual User UserTwo { get; set; }

    /// <summary>
    /// The id of one of the users in the DM channel
    /// </summary>
    [Column("user_one_id")]
    public long UserOneId { get; set; }

    /// <summary>
    /// The id of one of the users in the DM channel
    /// </summary>
    [Column("user_two_id")]
    public long UserTwoId { get; set; }

    [Column("message_count")]
    public long MessageCount { get; set; }

    /// <summary>
    /// Returns the direct chat channel with the given id
    /// </summary>
    public static async Task<DirectChatChannel> FindAsync(long id, ValourDB db)
        => await db.DirectChatChannels.FindAsync(id);

    /// <summary>
    /// Returns the direct chat channel between the two given user ids
    /// </summary>
    public static async Task<DirectChatChannel> FindAsync(long userOneId, long userTwoId, ValourDB db)
    {
        // Doesn't matter which user is which
        return await db.DirectChatChannels.FirstOrDefaultAsync(x =>
            (x.UserOneId == userOneId && x.UserTwoId == userTwoId) ||
            (x.UserOneId == userTwoId && x.UserOneId == userOneId)
        );
    }


    #region Routes

    [ValourRoute(HttpVerbs.Get), TokenRequired, InjectDb]
    [UserPermissionsRequired(UserPermissionsEnum.DirectMessages)]
    public static async Task<IResult> GetRoute(HttpContext ctx, long id)
    {
        // id is the id of the channel

        var db = ctx.GetDb();
        var channel = await FindAsync(id, db);

        if (channel is null)
            return ValourResult.NotFound<DirectChatChannel>();

        return Results.Json(channel);
    }

    [ValourRoute(HttpVerbs.Get, "/byuser/{id}", $"api/{nameof(DirectChatChannel)}"), TokenRequired, InjectDb]
    [UserPermissionsRequired(UserPermissionsEnum.DirectMessages)]
    public static async Task<IResult> GetViaTargetRoute(HttpContext ctx, long id)
    {
        // id is the id of the target user, not the channel!

        var db = ctx.GetDb();
        var token = ctx.GetToken();

        // Ensure target user exists
        if (!await db.Users.AnyAsync(x => x.Id == id))
            return ValourResult.NotFound("Target user not found");

        var channel = await FindAsync(token.UserId, id, db);

        // If there is no dm channel yet, we create it
        if (channel is null)
        {
            // TODO: Prevent if one of the users is blocking the other
            channel = new()
            {
                Id = IdManager.Generate(),
                UserOneId = token.UserId,
                UserTwoId = id,
                TimeLastActive = DateTime.UtcNow,
                MessageCount = 0
            };

            await db.AddAsync(channel);
            await db.SaveChangesAsync();
        }
            

        return Results.Json(channel);
    }



    #endregion
}
