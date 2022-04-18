using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
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
public class PlanetBan : Item, ISharedPlanetBan, IPlanetItemAPI<PlanetBan>, INodeSpecific
{
    [ForeignKey("Planet_Id")]
    [JsonIgnore]
    public virtual Planet Planet { get; set; }

    /// <summary>
    /// The user that was banned
    /// </summary>
    public ulong User_Id { get; set; }

    /// <summary>
    /// The planet the user was within
    /// </summary>
    public ulong Planet_Id { get; set; }

    /// <summary>
    /// The member that banned the user
    /// </summary>
    public ulong Banner_Id { get; set; }

    /// <summary>
    /// The user that was banned
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
    public async Task CreateAsync(ValourDB db)
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

    public async Task<TaskResult> CanGetAsync(PlanetMember member, ValourDB db)
    {
        if (member is null)
            return new TaskResult(false, "Member not found.");

        // Members can get their own ban info
        if (member.Id == Target_Id)
            return TaskResult.SuccessResult;

        if (!await member.HasPermissionAsync(PlanetPermissions.Ban, db))
            return new TaskResult(false, "Member lacks PlanetPermissions.Ban");

        return TaskResult.SuccessResult;
    }

    public Task<TaskResult> CanDeleteAsync(PlanetMember member, ValourDB db)
    {

    }

    public Task<TaskResult> CanUpdateAsync(PlanetMember member, ValourDB db)
        => CanCreateAsync(member, db);

    public async Task<TaskResult> CanCreateAsync(PlanetMember member, ValourDB db)
    {
        var canGet = await this.CanGetAsync(member, db);
        if (!canGet.Success)
            return canGet;

        

        if (Banner_Id != target.Id)
            return new TaskResult(false, $"You cannot ban yourself");


        if (await target.GetAuthorityAsync() >= await member.GetAuthorityAsync())
            return new TaskResult(false, "You cannot ban users with higher or same authority than your own.");

        return TaskResult.SuccessResult;
    }

    public async Task<TaskResult> ValidateItemAsync(PlanetBan old, ValourDB db)
    {
        // This role is new
        if (old is null)
        {
            // Ensure target exists
            PlanetMember target = await db.PlanetMembers.FirstOrDefaultAsync(x => x.Id == Target_Id);

            if (target is null)
                return new TaskResult(false, $"Target not found");

            if (Banner_Id != member.Id)
                return new TaskResult(false, $"The banner is not the same as the auth member");

            Time = DateTime.UtcNow;
        }
        else
        {
            if (this.User_Id != old.User_Id)
                return new TaskResult(false, "You cannot change who was banned");

            if (this.Banner_Id != old.Banner_Id)
                return new TaskResult(false, "You cannot change who banned the user");

            if (this.Time != old.Time)
                return new TaskResult(false, "You cannot change the creation time");
        }

        return TaskResult.SuccessResult;
    }
}