using System.ComponentModel.DataAnnotations.Schema;
using Valour.Database.Items.Planets;
using Valour.Database.Items.Planets.Channels;
using Valour.Database.Items.Planets.Members;

namespace Valour.Database.Items.Authorization;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

public class PermissionsNode : Shared.Items.Authorization.PermissionsNode<PermissionsNode>
{
    [ForeignKey("Planet_Id")]
    public virtual Planet Planet { get; set; }

    [ForeignKey("Role_Id")]
    public virtual PlanetRole Role { get; set; }

    /// <summary>
    /// This is a somewhat dirty way to fix the problem,
    /// but I need more time to figure out how to escape the generics hell
    /// i have created - spikey boy
    /// </summary>

    public async Task<IPlanetChannel> GetTargetAsync(ValourDB db)
    {
        switch (Target_Type)
        {
            case Shared.Items.ItemType.Channel: return await db.PlanetChatChannels.FindAsync(Target_Id);
            case Shared.Items.ItemType.Category: return await db.PlanetCategories.FindAsync(Target_Id);
        }

        return null;
    }
}
