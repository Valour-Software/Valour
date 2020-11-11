using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Valour.Shared.Messaging;

namespace Valour.Server.Messaging
{
    public class ServerMessage
    {
        [Key]
        public byte[] Hash { get; set; }

        /// <summary>
        /// Returns true if the client message matches this server message
        /// </summary>
        public bool EqualsMessage(ClientMessage message)
        {
            return (Hash == message.GetHash());
        }
    }
}
