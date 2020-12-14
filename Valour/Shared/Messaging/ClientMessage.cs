using System;
using System.Security.Cryptography;
using System.Text;

namespace Valour.Shared.Messaging
{
    public class ClientMessage
    {
        /// <summary>
        /// The user's ID, which is a GUID
        /// </summary>
        private Guid UserId { get; set; }

        /// <summary>
        /// String representation of message
        /// </summary>
        public string Content { get; set; }

        /// <summary>
        /// The time the message was sent (in UTC)
        /// </summary>
        public DateTime TimeSent { get; set; }

        /// <summary>
        /// Id of the channel this message belonged to
        /// </summary>
        public ulong ChannelId { get; set; }

        /// <summary>
        /// Index of the message
        /// </summary>
        public ulong Index { get; set; }

        /// <summary>
        /// Returns the hash for a message. Cannot be used in browser/client!
        /// </summary>
        public byte[] GetHash()
        {
            using (SHA256 sha = SHA256.Create())
            {
                string conc = $"{UserId}{Content}{TimeSent}{ChannelId}";

                byte[] buffer = Encoding.Unicode.GetBytes(conc);

                return sha.ComputeHash(buffer);
            }
        }
    }
}
