using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Valour.Server.Controllers
{
    /// <summary>
    /// Provides routes for user-related functions on the server side.
    /// </summary>
    [ApiController]
    [Route("[controller]/[action]")]
    public class UserController
    {
        /// <summary>
        /// Registers a new user and adds them to the database
        /// </summary>
        public async Task<string> RegisterUser(string username, string email, string password)
        {

        }

        /// <summary>
        /// Registers a new bot and adds them to the database
        /// </summary>
        public async Task<string> RegisterBot(string username, ulong owner_id, string password)
        {

        }
    }
}
