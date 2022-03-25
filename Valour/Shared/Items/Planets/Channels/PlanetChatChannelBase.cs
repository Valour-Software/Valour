using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Shared.Items.Planets.Channels;


/// <summary>
/// Represents a single chat channel within a planet
/// </summary>
public interface ISharedPlanetChatChannel
{
    /// <summary>
    /// The amount of messages ever sent in the channel
    /// </summary>
    [JsonPropertyName("MessageCount")]
    public ulong MessageCount { get; set; }

    /// <summary>
    /// If true, this channel will inherit the permission nodes
    /// from the category it belongs to
    /// </summary>
    [JsonPropertyName("InheritsPerms")]
    public bool InheritsPerms { get; set; }

}

