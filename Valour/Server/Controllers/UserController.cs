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
using Valour.Client.Users;
using Valour.Shared.Users.Identity;
using Newtonsoft.Json;
using Valour.Server.Roles;
using Valour.Client.Planets;

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
            this.Context = context;
            this.UserManager = userManager;
            this.Mapper = mapper;
        }

        /// <summary>
        /// Registers a new user and adds them to the database
        /// </summary>
        public async Task<TaskResult> RegisterUser(string username, string email, string password, string referrer = null)
        {
            if (username != null)
            {
                username = username.TrimEnd();
            }

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

            TaskResult<string> emailResult = TestEmail(email);

            if (!emailResult.Success)
            {
                return new TaskResult(false, emailResult.Message);
            }

            // This may fix broken email formatting
            email = emailResult.Data;

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

            Referral refer = null;

            if (!string.IsNullOrWhiteSpace(referrer))
            {
                User referUser = await Context.Users.FirstOrDefaultAsync(x => x.Username.ToLower() == referrer.ToLower());

                if (referUser == null)
                {
                    return new TaskResult(false, $"Failed: Could not find referrer {referrer}");
                }

                refer = new Referral()
                {
                    Referrer_Id = referUser.Id
                };
            }

            // At this point the safety checks are complete

            // Generate random salt
            byte[] salt = new byte[32];
            PasswordManager.GenerateSalt(salt);

            // Generate password hash
            byte[] hash = PasswordManager.GetHashForPassword(password, salt);

            // Create user object
            ServerUser user = new ServerUser()
            {
                Id = IdManager.Generate(),
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

            if (refer != null)
            {
                refer.User_Id = user.Id;
                await Context.Referrals.AddAsync(refer);
                await Context.SaveChangesAsync();
            }

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
            string emsg = $@"<body>
                              <h2 style='font-family:Helvetica;'>
                                Welcome to Valour!
                              </h2>
                              <p style='font-family:Helvetica;>
                                To verify your new account, please use the following link: 
                              </p>
                              <p style='font-family:Helvetica;'>
                                <a href='https://valour.gg/VerifyEmail/{code}'>Verify</a>
                              </p>
                            </body>";

            string rawmsg = $"Welcome to Valour!\nTo verify your new account, please go to the following link:\nhttps://valour.gg/VerifyEmail/{code}";

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
        public TaskResult<string> TestEmail(string email)
        {
            try
            {
                MailAddress address = new MailAddress(email);

                Console.WriteLine($"Email address: <{address.Address}>");

                return new TaskResult<string>(true, "Email was valid!", address.Address);
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
            AuthToken token = null;

            token = await Context.AuthTokens.FirstOrDefaultAsync(x => x.App_Id == "VALOUR" && x.User_Id == user.Id && x.Scope == UserPermissions.FullControl.Value);

            if (token == null)
            {
                // We now have to create a token for the user
                token = new AuthToken()
                {
                    App_Id = "VALOUR",
                    Id = Guid.NewGuid().ToString(),
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
            AuthToken authToken = await Context.AuthTokens.FindAsync(token);

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
            AuthToken authToken = await Context.AuthTokens.FindAsync(token);

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
