using Valour.Shared.Users;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2020 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Shared.Messaging
{
    public class ClientPlanetMessage : ClientMessage
    {
        /// <summary>
        /// The author of this message
        /// </summary>
        public PlanetUser Author { get; set; }
    }
}
