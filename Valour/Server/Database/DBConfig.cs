using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Valour.Server.Database
{
    /*  Valour - A free and secure chat client
     *  Copyright (C) 2020 Vooper Media LLC
     *  This program is subject to the GNU Affero General Public license
     *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
     */

    public class DBConfig
    {
        public static DBConfig instance;

        [JsonProperty]
        public string Host { get; set; }

        [JsonProperty]
        public string Password { get; set; }

        [JsonProperty]
        public string Username { get; set; }

        [JsonProperty]
        public string Database { get; set; }

        public DBConfig()
        {
            // Set main instance to the most recently created config
            instance = this;
        }
    }
}
