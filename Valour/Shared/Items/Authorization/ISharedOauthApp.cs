using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Valour.Shared.Items.Authorization;

/*  Valour - A free and secure chat client
*  Copyright (C) 2021 Vooper Media LLC
*  This program is subject to the GNU Affero General Public license
*  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
*/

/// <summary>
/// Oauth apps allow an organization or person to issue tokens on behalf of a user
/// which can be easily tracked and revoked
/// </summary>
public interface ISharedOauthApp
{
    ulong Id { get; set; }

    /// <summary>
    /// The secret key for the app
    /// </summary>
    string Secret { get; set; }

    /// <summary>
    /// The ID of the user that created this app
    /// </summary>
    ulong OwnerId { get; set; }

    /// <summary>
    /// The amount of times this app has been used
    /// </summary>
    int Uses { get; set; }

    /// <summary>
    /// The image used to represent the app
    /// </summary>
    string ImageUrl { get; set; }

    /// <summary>
    /// The name of the app
    /// </summary>
    string Name { get; set; }
}

