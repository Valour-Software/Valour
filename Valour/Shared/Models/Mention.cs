using System.Text.Json.Serialization;

namespace Valour.Shared.Models;

/*  Valour (TM) - A free and secure chat client
*  Copyright (C) 2025 Valour Software LLC
*  This program is subject to the GNU Affero General Public license
*  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
*/

public enum MentionType
{
    PlanetMember,
    Channel,
    Role,
    User,
}

/// <summary>
/// A member mention is used to refer to a member within a message
/// </summary>
public class Mention
{
    public static readonly Dictionary<char, MentionType> CharToMentionType = new()
    {
        { 'm', MentionType.PlanetMember },
        { 'u', MentionType.User },
        { 'c', MentionType.Channel },
        { 'r', MentionType.Role },
    };

    /// <summary>
    /// The id of the stored mention row
    /// </summary>
    [JsonPropertyName("Id")]
    public long Id { get; set; }

    /// <summary>
    /// The message this mention belongs to
    /// </summary>
    [JsonPropertyName("MessageId")]
    public long MessageId { get; set; }

    /// <summary>
    /// The position of the mention within the message mention list
    /// </summary>
    [JsonPropertyName("SortOrder")]
    public int SortOrder { get; set; }

    /// <summary>
    /// The type of mention this is
    /// </summary>
    [JsonPropertyName("Type")]
    public MentionType Type { get; set; }

    /// <summary>
    /// The item id being mentioned
    /// </summary>
    [JsonPropertyName("TargetId")]
    public long TargetId { get; set; }
}

