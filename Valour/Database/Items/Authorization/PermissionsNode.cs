using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Valour.Database.Items.Planets;
using Valour.Database.Items.Planets.Channels;
using Valour.Database.Items.Planets.Members;
using Valour.Shared.Authorization;
using Valour.Shared.Items;
using Valour.Shared.Items.Authorization;
using Valour.Shared.Items.Planets.Channels;

namespace Valour.Database.Items.Authorization;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

public class PermissionsNode : PermissionsNodeBase
{
    [ForeignKey("Planet_Id")]
    [JsonIgnore]
    public virtual Planet Planet { get; set; }

    [ForeignKey("Role_Id")]
    [JsonIgnore]
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
            case ItemType.ChatChannel: return await db.PlanetChatChannels.FindAsync(Target_Id);
            case ItemType.Category: return await db.PlanetCategories.FindAsync(Target_Id);
        }

        return null;
    }
}

