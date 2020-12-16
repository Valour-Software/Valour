using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Valour.Shared.Oauth
{
    /*  Valour - A free and secure chat client
     *  Copyright (C) 2020 Vooper Media LLC
     *  This program is subject to the GNU Affero General Public license
     *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
     */

    /// <summary>
    /// A class used to facilitate the transfer of authentication tokens
    /// </summary>
    public class TokenResponse
    {
        /// <summary>
        /// The result of the operation
        /// </summary>
        public TaskResult Result { get; set; }

        /// <summary>
        /// The resulting token
        /// </summary>
        public string Token { get; set; }

        public TokenResponse(string token, TaskResult result)
        {
            this.Result = result;
            this.Token = token;
        }
    }
}
