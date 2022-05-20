using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Valour.Database.Items.Authorization;
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

public class PlanetRoleMember : PlanetItem, ISharedPlanetRoleMember, INodeSpecific
{
    [ForeignKey("Member_Id")]
    [JsonIgnore]
    public virtual PlanetMember Member { get; set; }

    [ForeignKey("Role_Id")]
    [JsonIgnore]
    public virtual PlanetRole Role { get; set; }

    [ForeignKey("User_Id")]
    [JsonIgnore]
    public virtual User User { get; set; }

    public ulong User_Id { get; set; }
    public ulong Role_Id { get; set; }
    public ulong Member_Id { get; set; }

    public override ItemType ItemType => ItemType.PlanetRoleMember;

    public override async Task<TaskResult> CanCreateAsync(AuthToken token, PlanetMember member, ValourDB db)
    {
        // Needs to be able to GET in order to do anything else
        var canGet = await CanGetAsync(token, member, db);
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

        var targetMember = await db.PlanetMembers.FindAsync(Member_Id);

        if (targetMember is null)
            return new TaskResult(false, "Member not found.");

        if (targetMember.Planet_Id != Planet_Id)
            return new TaskResult(false, "Member Planet_Id mismatch.");

        if (targetMember.User_Id != User_Id)
            return new TaskResult(false, "Member User_Id mismatch.");

        if (role.Planet_Id != Planet_Id)
            return new TaskResult(false, "Role Planet_Id mismatch.");

        return TaskResult.SuccessResult;
    }

    public override async Task<TaskResult> CanDeleteAsync(AuthToken token, PlanetMember member, ValourDB db)
    {
        // Same permissions
        return await CanCreateAsync(token, member, db);
    } 

    public override async Task<TaskResult> CanUpdateAsync(AuthToken token, PlanetMember member, PlanetItem old, ValourDB db)
    {
        return await Task.FromResult(new TaskResult(false, "You cannot modify this object.")); 
    }
}

