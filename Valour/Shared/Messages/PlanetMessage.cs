using Newtonsoft.Json;
using System.Collections.Generic;
using Valour.Shared.Users;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Shared.Messages
{
    public class PlanetMessage : Message
    {

        private List<MemberMention> _member_mentions;

        /// <summary>
        /// The mentions for members within this message
        /// </summary>
        public List<MemberMention> MemberMentions
        {
            get
            {
                if (_member_mentions == null)
                {
                    if (MemberMentions_Data == null)
                    {
                        // Initialize with size 0 because this list should never grow.
                        _member_mentions = new List<MemberMention>(0);
                    }
                    else
                    {
                        // Deserialize mentions data
                        _member_mentions = JsonConvert.DeserializeObject<List<MemberMention>>(MemberMentions_Data);
                    }
                }

                return _member_mentions;
            }
        }



        /// <summary>
        /// Used for storing mention data for database use
        /// </summary>
        public string MemberMentions_Data { get; set; }

        public ulong Planet_Id { get; set; }
    }
}
