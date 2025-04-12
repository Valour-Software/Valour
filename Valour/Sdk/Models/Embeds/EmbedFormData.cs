﻿using System.Text.Json.Serialization;
using Valour.Sdk.Models.Messages.Embeds.Items;

namespace Valour.Sdk.Models.Messages.Embeds;

/*  Valour (TM) - A free and secure chat client
 *  Copyright (C) 2025 Valour Software LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

public class EmbedFormData
{
    [JsonPropertyName("ElementId")]
    public string ElementId { get; set; }

    [JsonPropertyName("Value")]
    public string Value { get; set; }

    [JsonPropertyName("Type")]
    public EmbedItemType Type { get; set; }
}