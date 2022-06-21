using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Shared.Items.Messages
{
    public interface ISharedMessage
    {
        /// <summary>
        /// The author's user ID
        /// </summary>
        ulong Author_Id { get; set; }

        /// <summary>
        /// The author's member ID
        /// </summary>
        ulong Member_Id { get; set; }

        /// <summary>
        /// String representation of message
        /// </summary>
        string Content { get; set; }

        /// <summary>
        /// The time the message was sent (in UTC)
        /// </summary>
        DateTime TimeSent { get; set; }

        /// <summary>
        /// Id of the channel this message belonged to
        /// </summary>
        ulong Channel_Id { get; set; }

        /// <summary>
        /// Index of the message
        /// </summary>
         ulong MessageIndex { get; set; }

        /// <summary>
        /// Data for representing an embed
        /// </summary>
        string EmbedData { get; set; }

        /// <summary>
        /// Data for representing mentions in a message
        /// </summary>
        string MentionsData { get; set; }

        /// <summary>
        /// Used to identify a message returned from the server 
        /// </summary>
        string Fingerprint { get; set; }

        /// <summary>
        /// Returns the hash for a message.
        /// </summary>
        public byte[] GetHash()
        {
            using (SHA256 sha = SHA256.Create())
            {
                string conc = $"{Author_Id}{Content}{TimeSent}{Channel_Id}{EmbedData}{MentionsData}";

                byte[] buffer = Encoding.Unicode.GetBytes(conc);

                return sha.ComputeHash(buffer);
            }
        }
    }
}
