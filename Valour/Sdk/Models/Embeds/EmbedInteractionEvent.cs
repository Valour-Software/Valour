﻿using System.Text.Json.Serialization;

namespace Valour.Sdk.Models.Messages.Embeds;

/*  Valour (TM) - A free and secure chat client
 *  Copyright (C) 2025 Valour Software LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

public enum EmbedIteractionEventType
{
    ItemClicked = 1,
    FormSubmitted = 2,
}

public class EmbedInteractionEvent
{
    [JsonPropertyName("FormId")]
    public string FormId { get; set; }

    [JsonPropertyName("ElementId")]
    public string ElementId { get; set; }

    [JsonPropertyName("EmbedIteractionEventType")]
    public EmbedIteractionEventType EventType { get; set; }

    [JsonPropertyName("PlanetId")]
    public long PlanetId { get; set; }

    [JsonPropertyName("MessageId")]
    public long MessageId { get; set; }

    [JsonPropertyName("Author_MemberId")]
    public long Author_MemberId { get; set; }

    [JsonPropertyName("MemberId")]
    public long MemberId { get; set; }

    [JsonPropertyName("ChannelId")]
    public long ChannelId { get; set; }

    [JsonPropertyName("TimeInteracted")]
    public DateTime TimeInteracted { get; set; }

    [JsonPropertyName("FormData")]
    public List<EmbedFormData> FormData { get; set; }
}

