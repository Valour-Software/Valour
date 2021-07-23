using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;

namespace Valour.Shared.Messages
{
    /*  Valour - A free and secure chat client
     *  Copyright (C) 2021 Vooper Media LLC
     *  This program is subject to the GNU Affero General Public license
     *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
     */

    public class EmbedFormDataItem
    {
        public string ElementId {get; set;}
        public string Value {get; set;}
        public string Type {get; set;}
    }

    public class InteractionEvent
    {
        public string Event {get; set;}
        public string ElementId {get; set;}
        public ulong Planet_Id {get; set;}
        public ulong Message_Id {get; set;}
        public ulong Message_Author_Member_Id {get; set;}
        public ulong Member_Id {get; set;}
        public ulong Channel_Id {get; set;}
        public DateTime TimeInteracted {get; set;}
        public List<EmbedFormDataItem> EmbedFormData {get; set;}
    }

    /// <summary>
    /// This class is only used by the server to check if a message is a embed
    /// </summary>

    public class EmbedItem
    {
        public string Type {get; set;}

        public string Text {get; set;}
        public string Name {get; set;}

        public string Color {get; set;}

        public bool Inline {get; set;}
    }

    /// <summary>
    /// This class is only used by the server to check if a message is a embed
    /// </summary>
    public class Embed
    {
        public string Color {get; set;}

        public List<EmbedItem> Items {get; set;}
    }
}