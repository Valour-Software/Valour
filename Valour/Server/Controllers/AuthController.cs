using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Valour.Server.Database;

namespace Valour.Server.Controllers
{

    /*  Valour - A free and secure chat client
     *  Copyright (C) 2021 Vooper Media LLC
     *  This program is subject to the GNU Affero General Public license
     *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
     */


    /// <summary>
    /// This controller is responsible for allowing authentification of users
    /// </summary>
    [ApiController]
    [Route("[controller]/[action]")]
    public class AuthController
    {
        /// <summary>
        /// Database context for controller
        /// </summary>
        private readonly ValourDB context;

        // Dependency injection
        public AuthController(ValourDB context)
        {
            this.context = context;
        }


    }
}
