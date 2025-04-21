﻿using System.Text.Json.Serialization;

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

