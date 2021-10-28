using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Valour.Server.Database;
using Valour.Shared;
using Valour.Shared.Oauth;
using Valour.Shared.Users;
using Valour.Shared.Users.Identity;

namespace Valour.Server.Users.Identity
{
    /*  Valour - A free and secure chat client
     *  Copyright (C) 2021 Vooper Media LLC
     *  This program is subject to the GNU Affero General Public license
     *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
     */

    public class UserManager
    {

        /// <summary>
        /// Validates and returns a User using credentials (async)
        /// </summary>
        public async Task<TaskResult<ServerUser>> ValidateAsync(string credential_type, string identifier, string secret)
        {
            using (ValourDB context  = new ValourDB(ValourDB.DBOptions))
            {
                // Find the credential that matches the identifier and type
                Credential credential = await context.Credentials.FirstOrDefaultAsync(
                    x => string.Equals(credential_type.ToUpper(), x.Credential_Type.ToUpper()) &&
                         string.Equals(identifier.ToUpper(), x.Identifier.ToUpper()));

                if (credential == null || string.IsNullOrWhiteSpace(secret))
                {
                    return new TaskResult<ServerUser>(false, "The credentials were incorrect.", null);
                }

                // Use salt to validate secret hash
                byte[] hash = PasswordManager.GetHashForPassword(secret, credential.Salt);

                // Spike needs to remember how reference types work 
                if (!hash.SequenceEqual(credential.Secret))
                {
                    return new TaskResult<ServerUser>(false, "The credentials were incorrect.", null);
                }

                ServerUser user = await context.Users.FindAsync(credential.User_Id);

                if (user.Disabled)
                {
                    return new TaskResult<ServerUser>(false, "This account has been disabled", null);
                }

                return new TaskResult<ServerUser>(true, "Succeeded", user);
            }
        }

        /// <summary>
        /// Validates and returns a User using credentials
        /// </summary>
        public TaskResult<ServerUser> Validate(string credential_type, string identifier, string secret)
        {
            return ValidateAsync(credential_type, identifier, secret).Result;
        }
    }
}
