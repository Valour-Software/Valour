using System.ComponentModel.DataAnnotations;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Database.Items.Planets.Members;

/// <summary>
/// This represents a user within a planet and is used to represent membership
/// </summary>
public class Ban
{
    /// <summary>
    /// The Id of this member object
    /// </summary>
    [Key]
    public ulong Id { get; set; }

    /// <summary>
    /// The user that was panned
    /// </summary>
    public ulong User_Id { get; set; }

    /// <summary>
    /// The planet the user was within
    /// </summary>
    public ulong Planet_Id { get; set; }

    /// <summary>
    /// The user that banned the user
    /// </summary>
    public ulong Banner_Id { get; set; }

    /// <summary>
    /// The reason for the ban
    /// </summary>
    public string Reason { get; set; }

    /// <summary>
    /// The time the ban was placed
    /// </summary>
    public DateTime Time { get; set; }

    /// <summary>
    /// The length of the ban
    /// </summary>
    public uint? Minutes { get; set; }

    /// <summary>
    /// True if the ban never expires
    /// </summary>
    public bool Permanent => Minutes == null;
}
