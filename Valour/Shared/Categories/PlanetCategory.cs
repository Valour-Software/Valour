using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Valour.Shared.Channels;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2020 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Shared.Categories
{
    /// <summary>
    /// Represents a single chat Category within a planet
    /// </summary>
    public class PlanetCategory : Valour.Shared.Planets.PlanetListItem
    {
        /// <summary>
        /// The Id of this category
        /// </summary>
        public ulong Id { get; set; }

        /// <summary>
        /// The name of this category
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The Id of the planet this category belongs to
        /// </summary>
        public ulong Planet_Id { get; set; }

        /// <summary>
        /// The id of the parent category, is null if theres no parent
        /// </summary>
        public ulong? Parent_Id { get; set;}

        /// <summary>
        /// Is the position in the category/channel list
        /// </summary>
        public ushort Position { get; set; }
    }
}
