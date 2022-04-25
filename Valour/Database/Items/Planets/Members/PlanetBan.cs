using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Valour.Database.Items.Authorization;
using Valour.Shared;
using Valour.Shared.Authorization;
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
public class PlanetBan : PlanetItem, ISharedPlanetBan, INodeSpecific
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

    /// <summary>
    /// Creates this item in the database and bans the user
    /// </summary>
    public override async Task CreateAsync(ValourDB db)
    {
        PlanetMember target = await db.PlanetMembers.FirstOrDefaultAsync(x => x.Id == Target_Id);

        var roles = db.PlanetRoleMembers.Where(x => x.Member_Id == target.Id && x.Planet_Id == Planet_Id);

        await db.PlanetRoleMembers.BulkDeleteAsync(roles);

        db.PlanetMembers.Remove(target);

        await db.AddAsync(this);
        await db.SaveChangesAsync();

        foreach (var role in roles)
        {
            PlanetHub.NotifyPlanetItemDelete(role);
        }

        PlanetHub.NotifyPlanetItemDelete(target);
        PlanetHub.NotifyPlanetItemChange(this);
    }

    public override async Task<TaskResult> CanGetAsync(AuthToken token, PlanetMember member, ValourDB db)
    {
        // Members can get their own ban info
        if (member.Id == Target_Id)
            return TaskResult.SuccessResult;

        return await CanBanAsync(member, db);
    }

    public override async Task<TaskResult> CanDeleteAsync(AuthToken token, PlanetMember member, ValourDB db) 
        => await CanBanAsync(member, db);

    public override async Task<TaskResult> CanUpdateAsync(AuthToken token, PlanetMember member, PlanetItem old, ValourDB db)
    {
        TaskResult canBan = await CanBanAsync(member, db);
        if (!canBan.Success)
            return canBan;

        var oldBan = old as PlanetBan;

        if (this.Target_Id != oldBan.Target_Id)
            return new TaskResult(false, "You cannot change who was banned");

        if (this.Banner_Id != oldBan.Banner_Id)
            return new TaskResult(false, "You cannot change who banned the user");

        if (this.Time != oldBan.Time)
            return new TaskResult(false, "You cannot change the creation time");

        return TaskResult.SuccessResult;
    }

    public override async Task<TaskResult> CanCreateAsync(AuthToken token, PlanetMember member, ValourDB db)
    {
        TaskResult canBan = await CanBanAsync(member, db);
        if (!canBan.Success)
            return canBan;

        // Ensure target exists
        PlanetMember target = await db.PlanetMembers.FirstOrDefaultAsync(x => x.User_Id == Target_Id);

        if (target is null)
            return new TaskResult(false, $"Target not found");

        if (Banner_Id == member.Id)
            return new TaskResult(false, $"You cannot ban yourself");

        if (await target.GetAuthorityAsync() >= await member.GetAuthorityAsync())
            return new TaskResult(false, "You cannot ban users with higher or same authority than your own.");

        if (Banner_Id != member.Id)
            return new TaskResult(false, $"The banner is not the same as the auth member");

        // Set time from server
        Time = DateTime.UtcNow;

        return TaskResult.SuccessResult;
    }
    
    private static async Task<TaskResult> CanBanAsync(PlanetMember member, ValourDB db) 
        => !await member.HasPermissionAsync(PlanetPermissions.Ban, db)
            ? new TaskResult(false, "Member lacks Planet Permission " + PlanetPermissions.Ban.Name)
            : TaskResult.SuccessResult;
}
