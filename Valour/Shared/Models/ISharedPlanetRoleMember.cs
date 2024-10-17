

/*  Valour (TM) - A free and secure chat client
 *  Copyright (C) 2024 Valour Software LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Shared.Models;

public interface ISharedPlanetRoleMember : ISharedPlanetModel<long>
{
    long UserId { get; set; }
    long RoleId { get; set; }
    long MemberId { get; set; }
}

