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
public class PlanetBan : Item, IPlanetItemAPI<PlanetBan>, INodeSpecific
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
    /// The reason for the ban
    /// </summary>
    public string Reason { get; set; }

    /// <summary>
    /// The time the ban was placed
    /// </summary>
    public DateTime Creation_Time { get; set; }

    /// <summary>
    /// The length of the ban
    /// </summary>
    public uint? Minutes { get; set; }

    /// <summary>
    /// The type of this item
    /// </summary>
    public override ItemType ItemType => ItemType.PlanetBan;

    /// <summary>
    /// True if the ban never expires
    /// </summary>
    public bool Permanent => Minutes is null;

    /// <summary>
    /// Creates this item in the database and bans the user
    /// </summary>
    public async Task CreateAsync(ValourDB db)
    {
        PlanetMember target = await db.PlanetMembers.FirstOrDefaultAsync(x => x.User_Id == this.User_Id);

        var roles = db.PlanetRoleMembers.Where(x => x.Member_Id == target.Id);

        db.PlanetRoleMembers.RemoveRange(roles);

        db.PlanetMembers.Remove(target);

        await db.AddAsync(this);
        await db.SaveChangesAsync();

        PlanetHub.NotifyPlanetItemChange(this);
    }

    /// <summary>
    /// Success if a member has invite permission
    /// and that the planet is private to get this 
    /// invite via the API.
    /// </summary>
    public async Task<TaskResult> CanGetAsync(PlanetMember member, ValourDB db)
    {
        if (member is null)
            return new TaskResult(false, "Member not found.");

        if (!await member.HasPermissionAsync(PlanetPermissions.Ban, db))
            return new TaskResult(false, "Member lacks PlanetPermissions.Ban");

        return TaskResult.SuccessResult;
    }

    public Task<TaskResult> CanDeleteAsync(PlanetMember member, ValourDB db)
        => Task.FromResult(TaskResult.SuccessResult);

    public Task<TaskResult> CanUpdateAsync(PlanetMember member, ValourDB db) 
        => Banner_Id != member.Id
            ? Task.FromResult(new TaskResult(false, $"The banner is not the same as the auth member"))
            : Task.FromResult(TaskResult.SuccessResult);

    public async Task<TaskResult> CanCreateAsync(PlanetMember member, ValourDB db)
    {
        var canGet = await this.CanGetAsync(member, db);
        if (!canGet.Success)
            return canGet;

        if (Banner_Id != member.Id)
            return new TaskResult(false, $"The banner is not the same as the auth member");

        PlanetMember target = await db.PlanetMembers.FirstOrDefaultAsync(x => x.User_Id == this.User_Id);

        if (target is null)
            return new TaskResult(false, $"Target not found");

        if (Banner_Id != target.Id)
            return new TaskResult(false, $"You cannot ban yourself");


        if (await target.GetAuthorityAsync() >= await member.GetAuthorityAsync())
            return new TaskResult(false, "You cannot manage roles with higher or same authority than your own.");

        return TaskResult.SuccessResult;
    }
    public Task<TaskResult> ValidateItemAsync(PlanetBan old, ValourDB db)
    {
        // This role is new
        if (old is null)
        {
            Creation_Time = DateTime.UtcNow;
        }
        else
        {
            if (this.User_Id != old.User_Id)
                return Task.FromResult(new TaskResult(false, "You cannot change who was banned"));
            if (this.Banner_Id != old.Banner_Id)
                return Task.FromResult(new TaskResult(false, "You cannot change who banned the user"));
            if (this.Creation_Time != old.Creation_Time)
                return Task.FromResult(new TaskResult(false, "You cannot change the creation time"));
        }

        if (Minutes == 0) Minutes = null;

        return Task.FromResult(TaskResult.SuccessResult);
    }
}