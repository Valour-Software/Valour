using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Valour.Shared.Items;
using Valour.Shared.Items.Planets;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Database.Items.Planets.Members;

/// <summary>
/// This represents a user within a planet and is used to represent membership
/// </summary>
public class PlanetBan : Item, ISharedPlanetBan
{
    /// <summary>
    /// The user that was panned
    /// </summary>
    [JsonPropertyName("User_Id")]
    public ulong User_Id { get; set; }

    /// <summary>
    /// The planet the user was within
    /// </summary>
    [JsonPropertyName("Planet_Id")]
    public ulong Planet_Id { get; set; }

    /// <summary>
    /// The user that banned the user
    /// </summary>
    [JsonPropertyName("Banner_Id")]
    public ulong Banner_Id { get; set; }

    /// <summary>
    /// The reason for the ban
    /// </summary>
    [JsonPropertyName("Reason")]
    public string Reason { get; set; }

    /// <summary>
    /// The time the ban was placed
    /// </summary>
    [JsonPropertyName("Time")]
    public DateTime Time { get; set; }

    /// <summary>
    /// The length of the ban
    /// </summary>
    [JsonPropertyName("Minutes")]
    public uint? Minutes { get; set; }

    /// <summary>
    /// True if the ban never expires
    /// </summary>
    public bool Permanent => Minutes == null;

    [JsonPropertyName("ItemType")]
    public override ItemType ItemType => ItemType.PlanetBan;
}
