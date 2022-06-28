using System;
using System.ComponentModel.DataAnnotations;

namespace Valour.Server.Database;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */


/// <summary>
/// This represents a user within a planet and is used to represent membership
/// </summary>
public class StatObject
{
    /// <summary>
    /// The Id of this object
    /// </summary>
    [Key]
    public ulong Id { get; set; }

    public int MessagesSent { get; set; }
    public int UserCount { get; set; }
    public int PlanetCount { get; set; }
    public int PlanetMemberCount { get; set; }
    public int ChannelCount { get; set; }
    public int CategoryCount { get; set; }
    public int Message24hCount { get; set; }

    public DateTime Time { get; set; }

    public StatObject()
    {

    }
}

