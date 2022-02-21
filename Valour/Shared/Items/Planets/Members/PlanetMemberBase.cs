using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Valour.Shared.Items;
using Valour.Shared.Items.Users;

namespace Valour.Shared.Items.Planets.Members;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */


/// <summary>
/// This represents a user within a planet and is used to represent membership
/// </summary>
public class PlanetMemberBase : Item, INamedItem, IPlanetItem
{
    public const int FLAG_UPDATE_ROLES = 0x01;

    /// <summary>
    /// The user within the planet
    /// </summary>
    [JsonPropertyName("User_Id")]
    public ulong User_Id { get; set; }

    /// <summary>
    /// The planet the user is within
    /// </summary>
    [JsonPropertyName("Planet_Id")]
    public ulong Planet_Id { get; set; }

    /// <summary>
    /// The name to be used within the planet
    /// </summary>
    [JsonPropertyName("Nickname")]
    public string Nickname { get; set; }

    /// <summary>
    /// The pfp to be used within the planet
    /// </summary>
    [JsonPropertyName("Member_Pfp")]
    public string Member_Pfp { get; set; }

    [NotMapped]
    public string Name
    {
        get => Nickname;
        set => Nickname = value;
    }

    [NotMapped]
    [JsonInclude]
    [JsonPropertyName("ItemType")]
    public override ItemType ItemType => ItemType.Member;
}

