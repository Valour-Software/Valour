using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Valour.Shared.Messages
{
    /*  Valour - A free and secure chat client
     *  Copyright (C) 2021 Vooper Media LLC
     *  This program is subject to the GNU Affero General Public license
     *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
     */

    public enum EmbedSize
    {
        Big,
        Normal,
        Small,
        VerySmall,
        Short,
        VeryShort
    }

    public enum EmbedItemType
    {
        Text,
        Button,
        InputBox
    }

    public class EmbedFormDataItem
    {
        [JsonPropertyName("Element_Id")]
        public string Element_Id { get; set; }

        [JsonPropertyName("Value")]
        public string Value { get; set; }

        [JsonPropertyName("Type")]
        public EmbedItemType Type { get; set; }
    }

    public class InteractionEvent
    {
        [JsonPropertyName("Event")]
        public string Event { get; set; }

        [JsonPropertyName("Element_Id")]
        public string Element_Id { get; set; }

        [JsonPropertyName("Planet_Id")]
        public ulong Planet_Id { get; set; }

        [JsonPropertyName("Message_Id")]
        public ulong Message_Id { get; set; }

        [JsonPropertyName("Author_Member_Id")]
        public ulong Author_Member_Id { get; set; }

        [JsonPropertyName("Member_Id")]
        public ulong Member_Id { get; set; }

        [JsonPropertyName("Channel_Id")]
        public ulong Channel_Id { get; set; }

        [JsonPropertyName("Time_Interacted")]
        public DateTime Time_Interacted { get; set; }

        [JsonPropertyName("Form_Data")]
        public List<EmbedFormDataItem> Form_Data { get; set; }
    }
}