namespace Valour.Shared.Models;

/*  Valour (TM) - A free and secure chat client
*  Copyright (C) 2024 Valour Software LLC
*  This program is subject to the GNU Affero General Public license
*  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
*/

/// <summary>
/// Oauth apps allow an organization or person to issue tokens on behalf of a user
/// which can be easily tracked and revoked
/// </summary>
public interface ISharedOauthApp : ISharedModel
{
    /// <summary>
    /// The secret key for the app
    /// </summary>
    string Secret { get; set; }

    /// <summary>
    /// The ID of the user that created this app
    /// </summary>
    long OwnerId { get; set; }

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

    /// <summary>
    /// The redirect for this app's authorization
    /// </summary>
    string RedirectUrl { get; set; }
}

