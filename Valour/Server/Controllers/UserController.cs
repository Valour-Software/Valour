using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Valour.Server.Database;
using Valour.Server.Email;
using Valour.Shared.Oauth;
using Valour.Server.Planets;
using Valour.Server.Users;
using Valour.Server.Users.Identity;
using Valour.Shared;
using Valour.Shared.Planets;
using Valour.Shared.Users;
using Valour.Client.Users;
using Valour.Shared.Users.Identity;
using Valour.Server.Roles;
using Valour.Client.Planets;
using Microsoft.AspNetCore.Http;
using Valour.Server.Oauth;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

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
        /// Database context
        /// </summary>
        private readonly ValourDB Context;
        private readonly UserManager UserManager;
        private readonly IMapper Mapper;

        // Dependency injection
        public UserController(ValourDB context, UserManager userManager, IMapper mapper)
        {
            Context = context;
            UserManager = userManager;
            Mapper = mapper;
        }

        /// <summary>
        /// Allows a token to be requested using basic login information
        /// </summary>
        public async Task<TaskResult<string>> RequestStandardToken(string email, string password)
        {
            UserEmail emailObj = await Context.UserEmails.Include(x => x.User).FirstOrDefaultAsync(x => x.Email == email.ToLower());

            if (emailObj == null)
            {
                return new TaskResult<string>(false, "There was no user found with that email.", null);
            }

            ServerUser user = emailObj.User;

            if (user.Disabled)
            {
                return new TaskResult<string>(false, "Your account has been disabled.", null);
            }

            bool authorized = false;

            if (!emailObj.Verified)
            {
                return new TaskResult<string>(false, "The email associated with this account needs to be verified! Please use the link that was emailed.", null);
            }

            var result = await UserManager.ValidateAsync(CredentialType.PASSWORD, email, password);

            if (result.Data != null && user.Id != result.Data.Id)
            {
                return new TaskResult<string>(false, "A critical error occured. This should not be possible. Seek help immediately.", null);
            }

            if (!result.Success)
            {
                Console.WriteLine($"Failed password validation for {email}");
                return new TaskResult<string>(false, result.Message, null);
            }

            authorized = true;


            // If the verification failed, forward the failure
            if (!authorized)
            {
                return new TaskResult<string>(false, "Failed to authorize user.", null);
            }

            // Check if there are any tokens already
            ServerAuthToken token = null;

            token = await Context.AuthTokens.FirstOrDefaultAsync(x => x.App_Id == "VALOUR" && x.User_Id == user.Id && x.Scope == UserPermissions.FullControl.Value);

            if (token == null)
            {
                // We now have to create a token for the user
                token = new ServerAuthToken()
                {
                    App_Id = "VALOUR",
                    Id = "val-" + Guid.NewGuid().ToString(),
                    Time = DateTime.UtcNow,
                    Expires = DateTime.UtcNow.AddDays(7),
                    Scope = UserPermissions.FullControl.Value,
                    User_Id = user.Id
                };

                using (ValourDB context = new ValourDB(ValourDB.DBOptions))
                {
                    await context.AuthTokens.AddAsync(token);
                    await context.SaveChangesAsync();
                }
            }
            else
            {
                token.Time = DateTime.UtcNow;
                token.Expires = DateTime.UtcNow.AddDays(7);

                using (ValourDB context = new ValourDB(ValourDB.DBOptions))
                {
                    context.AuthTokens.Update(token);
                    await context.SaveChangesAsync();
                }
            }

            return new TaskResult<string>(true, "Successfully verified and retrieved token!", token.Id);
        }

        /// <summary>
        /// Returns all user data using a token for verification
        /// </summary>
        public async Task<TaskResult<User>> GetUserWithToken(string token)
        {
            AuthToken authToken = await ServerAuthToken.TryAuthorize(token, Context);

            if (authToken == null)
            {
                return new TaskResult<User>(false, "Failed to verify token.", null);
            }

            ServerUser user = await Context.Users.FindAsync(authToken.User_Id);

            return new TaskResult<User>(true, "Retrieved user.", user);
        }

        /// <summary>
        /// Returns public user data using an id
        /// </summary>
        public async Task<TaskResult<User>> GetUser(ulong id)
        {
            User user = null;

            if (id == 0)
            {
                user = new User()
                {
                    Join_DateTime = DateTime.UtcNow,
                    Username = "Valour AI",
                    Bot = true
                };
            }
            else
            {
                user = await Context.Users.FindAsync(id);
            }

            return new TaskResult<User>(true, "Successfully found user.", user);
        }

        /// <summary>
        /// Returns the current latest client version
        /// </summary>
        public async Task<TaskResult<string>> GetCurrentVersion()
        {
            // This uses magic to work
            ClientPlanetMember user = new ClientPlanetMember();
            string version = user.GetType().Assembly.GetName().Version.ToString();
            return new TaskResult<string>(true, "Success", version);
        }

        /// <summary>
        /// Verifies the user email
        /// </summary>
        public async Task<TaskResult> VerifyEmail(string code)
        {
            EmailConfirmCode confirmCode = await Context.EmailConfirmCodes.Include(x => x.User).ThenInclude(x => x.Email).FirstOrDefaultAsync(x => x.Code == code);

            if (confirmCode == null)
            {
                return new TaskResult(false, "Could not find a valid code!");
            }

            var emailObj = confirmCode.User.Email;

            // At this point the email has been confirmed
            emailObj.Verified = true;

            Context.EmailConfirmCodes.Remove(confirmCode);
            await Context.SaveChangesAsync();

            return new TaskResult(true, "Successfully verified email.");
        }

        /// <summary>
        /// Revokes the token and effectively logs the user out
        /// </summary>
        public async Task<TaskResult> Logout(string token)
        {
            ServerAuthToken authToken = await ServerAuthToken.TryAuthorize(token, Context);

            if (authToken == null)
            {
                return new TaskResult(false, "Could not find token.");
            }

            Context.AuthTokens.Remove(authToken);
            await Context.SaveChangesAsync();

            return new TaskResult(true, "Logged out successfully.");
        }
    }
}
