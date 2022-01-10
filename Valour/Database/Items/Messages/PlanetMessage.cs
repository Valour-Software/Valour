using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Valour.Database.Items.Planets;
using Valour.Database.Items.Planets.Members;
using Valour.Database.Items.Users;
using Valour.Shared.Items;
using Valour.Shared.Items.Messages;

namespace Valour.Database.Items.Messages;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

public class PlanetMessage : PlanetMessageBase
{
    [ForeignKey("Planet_Id")]
    public Planet Planet { get; set;}

    [ForeignKey("Author_Id")]
    public User Author { get; set; }

    [ForeignKey("Member_Id")]
    public PlanetMember User { get; set; }
}

