using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Valour.Database.Items.Users;
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

public class PlanetRoleMember : IPlanetItem<PlanetRoleMember>, ISharedPlanetRoleMember, INodeSpecific
{
    [ForeignKey("Member_Id")]
    [JsonIgnore]
    public virtual PlanetMember Member { get; set; }

    [ForeignKey("Role_Id")]
    [JsonIgnore]
    public virtual PlanetRole Role { get; set; }

    [ForeignKey("Planet_Id")]
    [JsonIgnore]
    public virtual Planet Planet { get; set; }

    [ForeignKey("User_Id")]
    [JsonIgnore]
    public virtual User User { get; set; }

    public ulong User_Id { get; set; }
    public ulong Role_Id { get; set; }
    public ulong Planet_Id { get; set; }
    public ulong Member_Id { get; set; }

    public ItemType ItemType => ItemType.PlanetRoleMember;

    public async Task<TaskResult> CanCreateAsync(PlanetMember member, ValourDB db)
    {
        // Needs to be able to GET in order to do anything else
        var canGet = await ((IPlanetItem<PlanetRoleMember>)this).CanGetAsync(member, db);
        if (!canGet.Success)
            return canGet;

        if (!await member.HasPermissionAsync(PlanetPermissions.ManageRoles, db))
            return new TaskResult(false, "You lack the Planet Permission " + PlanetPermissions.ManageRoles.Name);

        var role = await db.PlanetRoles.FindAsync(Role_Id);

        if (role is null || role.Planet_Id != Planet_Id)
            return new TaskResult(false, "The Role_Id is invalid.");

        var memberAuthority = await member.GetAuthorityAsync();

        if (role.GetAuthority() >= memberAuthority)
            return new TaskResult(false, "You have less authority than the role you are trying to modify.");

        return new TaskResult(true, "Success");
    }

    public async Task<TaskResult> CanDeleteAsync(PlanetMember member, ValourDB db)
    {
        // Same permissions
        return await CanCreateAsync(member, db);
    }

    public async Task<TaskResult> CanUpdateAsync(PlanetMember member, ValourDB db)
    {
        // Same permissions
        return await CanCreateAsync(member, db);
    }

    public async Task<TaskResult> ValidateItemAsync(PlanetRoleMember old, ValourDB db)
    {
        if (old != null)
            return new TaskResult(false, "You cannot modify this object.");

        var member = await db.PlanetMembers.FindAsync(Member_Id);

        if (member is null)
            return new TaskResult(false, "Member not found.");

        if (member.Planet_Id != Planet_Id)
            return new TaskResult(false, "Member Planet_Id mismatch.");

        if (member.User_Id != User_Id)
            return new TaskResult(false, "Member User_Id mismatch.");

        var role = await db.PlanetRoles.FindAsync(Role_Id);

        if (role is null)
            return new TaskResult(false, "Role not found.");

        if (role.Planet_Id != Planet_Id)
            return new TaskResult(false, "Role Planet_Id mismatch.");

        return TaskResult.SuccessResult;
    }
}

