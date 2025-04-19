namespace Valour.Shared.Models;

/*  Valour (TM) - A free and secure chat client
 *  Copyright (C) 2025 Valour Software LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */


/// <summary>
/// This represents a user within a planet and is used to represent membership
/// </summary>
public interface ISharedPlanetInvite : ISharedPlanetModel<string>
{
    public const string BaseRoute = "api/invites";
    public static string GetIdRoute(string id) => $"{BaseRoute}/{id}";
    
    /// <summary>
    /// The user that created the invite
    /// </summary>
    long IssuerId { get; set; }

    /// <summary>
    /// The time the invite was created
    /// </summary>
    DateTime TimeCreated { get; set; }

    /// <summary>
    /// The time when this invite expires. Null for never.
    /// </summary>
    DateTime? TimeExpires { get; set; }

    public bool IsPermanent() =>
        ISharedPlanetInvite.IsPermanent(this);

    public static bool IsPermanent(ISharedPlanetInvite item) =>
        item.TimeExpires == null;
}

