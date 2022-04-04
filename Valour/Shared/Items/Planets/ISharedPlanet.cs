using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Shared.Items.Planets;

public interface ISharedPlanet
{
    /// <summary>
    /// The Id of the owner of this planet
    /// </summary>
    ulong Owner_Id { get; set; }

    /// <summary>
    /// The name of this planet
    /// </summary>
    string Name { get; set; }

    /// <summary>
    /// The image url for the planet 
    /// </summary>
    string Image_Url { get; set; }

    /// <summary>
    /// The description of the planet
    /// </summary>
    string Description { get; set; }

    /// <summary>
    /// If the server requires express allowal to join a planet
    /// </summary>
    bool Public { get; set; }

    /// <summary>
    /// The default role for the planet
    /// </summary>
    ulong Default_Role_Id { get; set; }

    /// <summary>
    /// The id of the main channel of the planet
    /// </summary>
    ulong Main_Channel_Id { get; set; }
}

