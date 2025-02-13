namespace Valour.Shared.Models;

/*  Valour (TM) - A free and secure chat client
 *  Copyright (C) 2024 Valour Software LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */


/// <summary>
/// This represents a user within a planet and is used to represent membership
/// </summary>
public interface ISharedPlanetMember : ISharedPlanetModel<long>
{
    const string BaseRoute = "api/members";
    
    /// <summary>
    /// The user within the planet
    /// </summary>
    long UserId { get; set; }

    /// <summary>
    /// The name to be used within the planet
    /// </summary>
    string Nickname { get; set; }

    /// <summary>
    /// The pfp to be used within the planet
    /// </summary>
    string MemberAvatar { get; set; }

    public PlanetRoleMembership RoleMembership { get; set; }
    
    public static TaskResult ValidateName(ISharedPlanetMember member)
    {
        // Ensure nickname is valid
        return member.Nickname.Length > 32 ? new TaskResult(false, "Maximum nickname is 32 characters.") : 
            TaskResult.SuccessResult;
    }
}

