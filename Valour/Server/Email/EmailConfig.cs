using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Valour.Server.Email
{
    /*  Valour - A free and secure chat client
     *  Copyright (C) 2020 Vooper Media LLC
     *  This program is subject to the GNU Affero General Public license
     *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
     */

    public class EmailConfig
    {
        /// <summary>
        /// The current Email Configuration
        /// </summary>
        public static EmailConfig instance;

        /// <summary>
        /// The API key for the email service
        /// </summary>
        [JsonProperty]
        public string Api_Key { get; set; }

        /// <summary>
        /// Set instance to newest config
        /// </summary>
        public EmailConfig()
        {
            instance = this;
        }
    }
}
