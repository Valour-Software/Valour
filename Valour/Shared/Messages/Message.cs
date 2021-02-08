using System;
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Shared.Messages
{
    public class Message
    {

        /// <summary>
        /// The Id of the message
        /// </summary>
        [Key]
        public ulong Id { get; set; }

        /// <summary>
        /// The user's ID
        /// </summary>
        public ulong Author_Id { get; set; }

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
        public ulong Channel_Id { get; set; }

        /// <summary>
        /// Index of the message
        /// </summary>
        public ulong Message_Index { get; set; }

        /// <summary>
        /// Returns the hash for a message. Cannot be used in browser/client!
        /// </summary>
        public byte[] GetHash()
        {
            using (SHA256 sha = SHA256.Create())
            {
                string conc = $"{Author_Id}{Content}{TimeSent}{Channel_Id}";

                byte[] buffer = Encoding.Unicode.GetBytes(conc);

                return sha.ComputeHash(buffer);
            }
        }
    }
}
