using System;
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.Json;
using Valour.Shared.Messages.Embeds;
using Valour.Shared.Messages.Mentions;

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
        [JsonPropertyName("Id")]
        public ulong Id { get; set; }

        /// <summary>
        /// The user's ID
        /// </summary>
        [JsonPropertyName("Author_Id")]
        public ulong Author_Id { get; set; }

        /// <summary>
        /// The member's ID
        /// </summary>
        [JsonPropertyName("Author_Id")]
        public ulong Member_Id { get; set; }

        /// <summary>
        /// String representation of message
        /// </summary>
        [JsonPropertyName("Content")]
        public string Content { get; set; }

        /// <summary>
        /// The time the message was sent (in UTC)
        /// </summary>
        [JsonPropertyName("TimeSent")]
        public DateTime TimeSent { get; set; }

        /// <summary>
        /// Id of the channel this message belonged to
        /// </summary>
        [JsonPropertyName("Channel_Id")]
        public ulong Channel_Id { get; set; }

        /// <summary>
        /// Index of the message
        /// </summary>
        [JsonPropertyName("Message_Index")]
        public ulong Message_Index { get; set; }

        /// <summary>
        /// Data for representing an embed
        /// </summary>
        [JsonPropertyName("Embed_Data")]
        public string Embed_Data { get; set; }

        /// <summary>
        /// Data for representing mentions in a message
        /// </summary>
        [JsonPropertyName("Mentions_Data")]
        public string Mentions_Data { get; set; }

        /// <summary>
        /// Used to identify a message returned from the server 
        /// </summary>
        [JsonInclude]
        [JsonPropertyName("Fingerprint")]
        public string Fingerprint;

        /// <summary>
        /// The mentions contained within this message
        /// </summary>
        private List<Mention> _mentions;

        /// <summary>
        /// True if the mentions data has been parsed
        /// </summary>
        private bool mentionsParsed = false;

        /// <summary>
        /// The inner embed data
        /// </summary>
        private Embed _embed;

        /// <summary>
        /// True if the embed data has been parsed
        /// </summary>
        private bool embedParsed = false;

        /// <summary>
        /// The mentions for members within this message
        /// </summary>
        public List<Mention> Mentions
        {
            get
            {
                if (!mentionsParsed)
                {
                    if (!string.IsNullOrEmpty(Mentions_Data))
                    {
                        _mentions = JsonSerializer.Deserialize<List<Mention>>(Mentions_Data);
                    }
                }

                return _mentions;
            }
        }

        public Embed Embed
        {
            get
            {
                if (!embedParsed)
                {
                    if (!string.IsNullOrEmpty(Embed_Data))
                    {
                        _embed = JsonSerializer.Deserialize<Embed>(Embed_Data);
                    }

                    embedParsed = true;
                }

                return _embed;
            }
        }

        public void ClearMentions()
        {
            if (_mentions == null)
            {
                _mentions = new List<Mention>();
            }
            else
            {
                _mentions.Clear();
            }
        }

        public void SetMentions(IEnumerable<Mention> mentions)
        {
            _mentions = mentions.ToList();
            Mentions_Data = JsonSerializer.Serialize(mentions);
        }

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
        /// <summary>
        /// Returns true if the message is a embed
        /// </summary>
        public bool IsEmbed()
        {
            if (Embed_Data != null) {
                return true;
            }
            else {
                return false;
            }
        }
    }
}
