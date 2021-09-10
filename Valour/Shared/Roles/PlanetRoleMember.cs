using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Shared.Roles
{
    public class PlanetRoleMember
    {
        [JsonPropertyName("Id")]
        public ulong Id { get; set; }

        [JsonPropertyName("User_Id")]
        public ulong User_Id { get; set; }

        [JsonPropertyName("Role_Id")]
        public ulong Role_Id { get; set; }

        [JsonPropertyName("Planet_Id")]
        public ulong Planet_Id { get; set; }

        [JsonPropertyName("Member_Id")]
        public ulong Member_Id { get; set; }
    }
}
