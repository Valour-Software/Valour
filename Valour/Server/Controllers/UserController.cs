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
using Valour.Server.Oauth;
using Valour.Server.Planets;
using Valour.Server.Users;
using Valour.Server.Users.Identity;
using Valour.Shared;
using Valour.Shared.Oauth;
using Valour.Shared.Planets;
using Valour.Shared.Users;
using Valour.Shared.Users.Identity;
using Newtonsoft.Json;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2020 Vooper Media LLC
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
            this.Context = context;
            this.UserManager = userManager;
            this.Mapper = mapper;
        }

        public async Task<TaskResult> KickUser(ulong id, ulong Planet_Id, ulong userid, string token)
        {
            AuthToken authToken = await Context.AuthTokens.FindAsync(token);

            // Return the same if the token is for the wrong user to prevent someone
            // from knowing if they cracked another user's token. This is basically 
            // impossible to happen by chance but better safe than sorry in the case that
            // the literal impossible odds occur, more likely someone gets a stolen token
            // but is not aware of the owner but I'll shut up now - Spike
            if (authToken == null || authToken.User_Id != userid)
            {
                return new TaskResult(false, "Failed to authorize user.");
            }

            PlanetMember member = await Context.PlanetMembers.Where(x => x.User_Id == id && x.Planet_Id == Planet_Id).FirstOrDefaultAsync();

            Context.PlanetMembers.Remove(member);

            await Context.SaveChangesAsync();

            return null;
        }

        /// <summary>
        /// Registers a new user and adds them to the database
        /// </summary>
        public async Task<TaskResult> RegisterUser(string username, string email, string password)
        {
            // Ensure unique username
            if (await Context.Users.AnyAsync(x => x.Username.ToLower() == username.ToLower()))
            {
                return new TaskResult(false, $"Failed: There was already a user named {username}");
            }

            // Ensure unique email
            if (await Context.UserEmails.AnyAsync(x => x.Email.ToLower() == email.ToLower()))
            {
                return new TaskResult(false, $"Failed: There was already a user using the email {email}");
            }

            TaskResult emailResult = TestEmail(email);

            if (!emailResult.Success)
            {
                return emailResult;
            }

            TaskResult usernameResult = TestUsername(username);

            if (!usernameResult.Success)
            {
                return usernameResult;
            }

            // Test password complexity
            TaskResult passwordResult = PasswordManager.TestComplexity(password);

            // Enforce password tests
            if (!passwordResult.Success)
            {
                return passwordResult;
            }

            // At this point the safety checks are complete

            // Generate random salt
            byte[] salt = new byte[32];
            PasswordManager.GenerateSalt(salt);

            // Generate password hash
            byte[] hash = PasswordManager.GetHashForPassword(password, salt);

            // Create user object
            User user = new User()
            {
                Username = username,
                Join_DateTime = DateTime.UtcNow
            };

            // An error here would be really bad so we'll be careful and catch any exceptions
            try
            {
                await Context.Users.AddAsync(user);
                await Context.SaveChangesAsync();
            }
            catch (System.Exception e)
            {
                return new TaskResult(false, $"A critical error occured adding the user.");
            }

            // Create email object
            UserEmail emailObj = new UserEmail()
            {
                Email = email,
                Verified = false,
                User_Id = user.Id
            };

            try
            {
                // Pray something doesnt break between these
                await Context.UserEmails.AddAsync(emailObj);
                await Context.SaveChangesAsync();
            }
            catch (System.Exception e)
            {
                return new TaskResult(false, $"A critical error occured adding the email.");
            }

            Credential cred = new Credential()
            {
                Credential_Type = CredentialType.PASSWORD,
                Identifier = email,
                Salt = salt,
                Secret = hash,
                User_Id = user.Id // We need to find what the user's assigned ID is (auto-filled by EF?)
            };

            // An error here would be really bad so we'll be careful and catch any exceptions
            try
            {
                await Context.Credentials.AddAsync(cred);
                await Context.SaveChangesAsync();
            }
            catch (System.Exception e)
            {
                return new TaskResult(false, $"A critical error occured adding the credentials.");
            }

            string code = Guid.NewGuid().ToString();

            EmailConfirmCode emailConfirm = new EmailConfirmCode()
            {
                Code = code,
                User_Id = user.Id
            };

            // An error here would be really bad so we'll be careful and catch any exceptions
            try
            {
                await Context.EmailConfirmCodes.AddAsync(emailConfirm);
                await Context.SaveChangesAsync();
            }
            catch (System.Exception e)
            {
                return new TaskResult(false, $"A critical error occured adding the email confirmation code.");
            }


            // Send registration email
            string emsg = $@"<body style='background-color:#040D14'>
                              <h2 style='font-family:Helvetica; color:white'>
                                Welcome to Valour!
                              </h2>
                              <p style='font-family:Helvetica; color:white'>
                                To verify your new account, please use this code as your password the first time you log in: 
                              </p>
                              <p style='font-family:Helvetica; color:#88ffff'>
                                {code}
                              </p>
                            </body>";

            string rawmsg = $"Welcome to Valour!\nTo verify your new account, please use this code as your password the first time you log in:\n{code}";

            await EmailManager.SendEmailAsync(email, "Valour Registration", rawmsg, emsg);

            return new TaskResult(true, $"Successfully created user {username}");
        }

        /// <summary>
        /// Registers a new bot and adds them to the database
        /// </summary>
        //public async Task<string> RegisterBot(string username, ulong owner_id, string password)
        //{
        //   TODO
        //}

        /// <summary>
        /// Allows for checking if a password meets standards though API
        /// </summary>
        public async Task<TaskResult> TestPasswordComplexity(string password)
        {
            // Regex can be slow, so we throw it in another thread
            return await Task.Run(() => PasswordManager.TestComplexity(password));
        }

        /// <summary>
        /// Allows checking if a email meets standards
        /// </summary>
        public TaskResult TestEmail(string email)
        {
            try
            {
                MailAddress address = new MailAddress(email);
                return new TaskResult(true, "Email was valid!");
            }
            catch (FormatException e)
            {
                return new TaskResult(false, "Email was invalid.");
            }
        }

        public Regex usernameRegex = new Regex(@"^[a-zA-Z0-9_-]+$");

        public TaskResult TestUsername(string username)
        {
            if (username.Length > 32)
            {
                return new TaskResult(false, "That username is too long!");
            }

            if (!usernameRegex.IsMatch(username))
            {
                return new TaskResult(false, "Usernames must be alphanumeric plus underscores and dashes.");
            }

            return new TaskResult(true, "The given username is valid.");
        }

        /// <summary>
        /// Allows a token to be requested using basic login information
        /// </summary>
        public async Task<TaskResult<string>> RequestStandardToken(string email, string password)
        {
            UserEmail emailObj = await Context.UserEmails.FindAsync(email.ToLower());

            if (emailObj == null)
            {
                return new TaskResult<string>(false, "There was no user found with that email.", null);
            }

            User user = await emailObj.GetUserAsync();

            if (user.Disabled)
            {
                return new TaskResult<string>(false, "Your account has been disabled.", null);
            }

            bool authorized = false;

            if (!emailObj.Verified)
            {
                EmailConfirmCode confirmCode = await Context.EmailConfirmCodes.FindAsync(password);

                // Someone using another person's verification is a little
                // worrying, and we don't want them to know it worked, so we'll
                // send the same error either way.
                if (confirmCode == null || confirmCode.User_Id != user.Id)
                {
                    return new TaskResult<string>(false, "The email associated with this account needs to be verified! Please log in using the code " +
                        "that was emailed as your password.", null);
                }

                // At this point the email has been confirmed
                emailObj.Verified = true;

                Context.EmailConfirmCodes.Remove(confirmCode);
                await Context.SaveChangesAsync();

                authorized = true;
            }
            else
            {

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
            }

            // If the verification failed, forward the failure
            if (!authorized)
            {
                return new TaskResult<string>(false, "Failed to authorize user.", null);
            }

            // Check if there are any tokens already
            AuthToken token = null;

            token = await Context.AuthTokens.FirstOrDefaultAsync(x => x.App_Id == "VALOUR" && x.User_Id == user.Id && x.Scope == Permission.FullControl.Value);

            if (token == null)
            {
                // We now have to create a token for the user
                token = new AuthToken()
                {
                    App_Id = "VALOUR",
                    Id = Guid.NewGuid().ToString(),
                    Time = DateTime.UtcNow,
                    Expires = DateTime.UtcNow.AddDays(7),
                    Scope = Permission.FullControl.Value,
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
            AuthToken authToken = await Context.AuthTokens.FindAsync(token);

            if (authToken == null)
            {
                return new TaskResult<User>(false, "Failed to verify token.", null);
            }

            User user = await Context.Users.FindAsync(authToken.User_Id);

            return new TaskResult<User>(true, "Retrieved user.", user);
        }

        /// <summary>
        /// Returns public user data using an id
        /// </summary>
        public async Task<TaskResult<User>> GetUser(ulong id)
        {
            // Get user data
            User user = await Context.Users.FindAsync(id);

            return new TaskResult<User>(true, "Successfully found user.", user);
        }

        /// <summary>
        /// Returns a planetuser given the user and planet id
        /// </summary>
        public async Task<TaskResult<PlanetUser>> GetPlanetUser(ulong userid, ulong planet_id, string auth)
        {
            // Retrieve planet
            ServerPlanet planet = ServerPlanet.FromBase(await Context.Planets.FindAsync(planet_id), Mapper);

            if (planet == null) return new TaskResult<PlanetUser>(false, "The planet could not be found.", null);

            // Authentication flow
            AuthToken token = await Context.AuthTokens.FindAsync(auth);

            // If authorizor is not a member of the planet, they do not have authority to get member info
            if (token == null || !(await planet.IsMemberAsync(token.User_Id))){
                return new TaskResult<PlanetUser>(false, "Failed to authorize.", null);
            }

            // At this point the request is authorized

            // Retrieve server data for user
            User user = await Context.Users.FindAsync(userid);

            // Null check
            if (user == null) return new TaskResult<PlanetUser>(false, "The user could not be found.", null);

            // Ensure the user is a member of the planet
            if (!(await planet.IsMemberAsync(user)))
            {
                return new TaskResult<PlanetUser>(false, "The target user is not a member of the planet.", null);
            }

            PlanetUser planetUser = await ServerPlanetUser.CreateAsync(userid, planet_id, Mapper);

            if (planetUser == null) return new TaskResult<PlanetUser>(false, "Could not create planet user: Fatal error.", null);

            return new TaskResult<PlanetUser>(true, "Successfully retrieved planet user.", planetUser);
        }

        /// <summary>
        /// Returns the planet membership of a user
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task<TaskResult<List<Planet>>> GetPlanetMembership(ulong id, string token)
        {
            if (token == null)
            {
                return new TaskResult<List<Planet>>(false, "Please supply an authentication token", null);
            }

            AuthToken authToken = await Context.AuthTokens.FindAsync(token);

            if (authToken.User_Id != id)
            {
                return new TaskResult<List<Planet>>(false, $"Could not authenticate for user {id}", null);
            }

            if (!Permission.HasPermission(authToken.Scope, UserPermissions.Membership))
            {
                return new TaskResult<List<Planet>>(false, $"The given token does not have membership scope", null);
            }

            List<Planet> membership = new List<Planet>();

            foreach(PlanetMember member in Context.PlanetMembers.Where(x => x.User_Id == id))
            {
                Planet planet = await Context.Planets.FindAsync(member.Planet_Id);

                if (planet != null)
                {
                    membership.Add(planet);
                }
            }

            return new TaskResult<List<Planet>>(true, $"Retrieved {membership.Count} planets", membership);
        }
    }
}
