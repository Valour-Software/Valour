using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Valour.Shared.Messaging
{
    public class MessagePostResponse : TaskResult
    {
        /// <summary>
        /// The final index of the message that was posted
        /// </summary>
        public ulong Index { get; set; }

        public MessagePostResponse(bool success, string response, ulong index) : base(success, response)
        {
            this.Index = index;
        }
    }
}
