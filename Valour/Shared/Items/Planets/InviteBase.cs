using System.Text.Json.Serialization;

namespace Valour.Shared.Items.Planets;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */


/// <summary>
/// This represents a user within a planet and is used to represent membership
/// </summary>
public class InviteBase : ISharedItem
{
    /// <summary>
    /// the invite code
    /// </summary>
    [JsonPropertyName("Code")]
    public string Code { get; set; }

    /// <summary>
    /// The planet the invite is for
    /// </summary>
    [JsonPropertyName("Planet_Id")]
    public ulong Planet_Id { get; set; }

    /// <summary>
    /// The user that created the invite
    /// </summary>
    [JsonPropertyName("Issuer_Id")]
    public ulong Issuer_Id { get; set; }

    /// <summary>
    /// The time the invite was created
    /// </summary>
    [JsonPropertyName("Issued")]
    public DateTime Issued { get; set; }

    /// <summary>
    /// The time when this invite expires. Null for never.
    /// </summary>
    [JsonPropertyName("Expires")]
    public DateTime? Expires { get; set; }

    public bool IsPermanent()
    {
        return (Expires == null);
    }

    [JsonInclude]
    [JsonPropertyName("ItemType")]
    public override ItemType ItemType => ItemType.Invite;
}

