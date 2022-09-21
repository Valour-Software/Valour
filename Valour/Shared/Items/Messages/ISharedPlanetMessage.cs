using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Valour.Shared.Items.Planets;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Shared.Items.Messages
{
    public interface ISharedPlanetMessage : ISharedMessage, ISharedPlanetItem
    {
        /// <summary>
        /// The author's member ID
        /// </summary>
        long AuthorMemberId { get; set; }
    }
}
