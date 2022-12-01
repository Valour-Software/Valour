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
[Table("stat_objects")]
public class StatObject
{
    /// <summary>
    /// The Id of this object
    /// </summary>
    [Key]
    [Column("id")]
    public long Id {get; set; }

    [Column("messages_sent")]
    public int MessagesSent { get; set; }

    [Column("user_count")]
    public int UserCount { get; set; }

    [Column("planet_count")]
    public int PlanetCount { get; set; }

    [Column("planet_member_count")]
    public int PlanetMemberCount { get; set; }

    [Column("channel_count")]
    public int ChannelCount { get; set; }

    [Column("category_count")]
    public int CategoryCount { get; set; }

    [Column("message_day_count")]
    public int MessageDayCount { get; set; }

    [Column("time_created")] 
    public DateTime TimeCreated { get; set; }
}

