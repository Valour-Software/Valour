using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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

public class PlanetRoleMember : PlanetItem, ISharedPlanetRoleMember
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

    // This doesn't really even need API routes, it's just used internally to map roles to members.
    // Use a route from PlanetMember if you need to get someone's roles.
}

