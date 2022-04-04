using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Valour.Shared.Items;
using Valour.Shared.Items.Planets.Members;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Database.Items.Planets.Members;

public class PlanetRoleMember : PlanetRoleMemberBase, INodeSpecific
{
    [ForeignKey("Member_Id")]
    [JsonIgnore]
    public virtual PlanetMember Member { get; set; }

    [ForeignKey("Role_Id")]
    [JsonIgnore]
    public virtual PlanetRole Role { get; set; }
}

