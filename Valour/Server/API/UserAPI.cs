using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Valour.Database;
using Valour.Database.Items.Users;
using Valour.Server.Email;
using Valour.Server.Extensions;
using Valour.Server.Users.Identity;
using Valour.Shared;
using Valour.Shared.Items.Users;
using Valour.Shared.Users.Identity;
using Valour.Database.Items.Authorization;
using Valour.Shared.Authorization;
using Valour.Server.Users;
using Valour.Database.Items.Planets.Members;
using Valour.Shared.Http;

namespace Valour.Server.API
{
    public class UserAPI : BaseAPI
    {
        public static void AddRoutes(WebApplication app)
        {
            app.MapGet("api/user/{user_id}", GetUser);

            app.MapGet("api/user/{user_id}/planets", GetPlanets);

            app.MapGet("api/user/{user_id}/planet_ids", GetPlanetIds);

            app.MapPost("api/user/register", RegisterUser);

            app.MapPost("api/user/passwordreset", PasswordReset);

            app.MapPost("api/user/recover", RecoverPassword);

            app.MapPost("api/user/requesttoken", RequestToken);

            app.MapPost("api/user/logout", LogOut);

            app.MapPost("api/user/withtoken", WithToken);

            app.MapPost("api/user/verify/{code}", VerifyEmail);
        }

        private static async Task<IResult> GetUser(HttpContext ctx, ValourDB db, ulong user_id, [FromHeader] string authorization)
        {
            var authToken = await AuthToken.TryAuthorize(authorization, db);
            if (authToken == null)
                return ValourResult.NoToken();

            var user = await db.Users.FindAsync(user_id);

            if (user == null)
                return ValourResult.NotFound<User>();

            return Results.Json(user);
        }

        private static async Task VerifyEmail(HttpContext ctx, ValourDB db, string code)
        {
            var confirmCode = await db.EmailConfirmCodes
                .Include(x => x.User)
                .ThenInclude(x => x.Email)
                .FirstOrDefaultAsync(x => x.Code == code);

            if (confirmCode == null) { await NotFound("Code not found", ctx); return; }

            var email = confirmCode.User.Email;

            email.Verified = true;

            db.EmailConfirmCodes.Remove(confirmCode);
            await db.SaveChangesAsync();

            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsync("Success");
        }

        private static async Task LogOut(HttpContext ctx, ValourDB db, [FromHeader] string authorization)
        {
            var authToken = await AuthToken.TryAuthorize(authorization, db);

            if (authToken == null) { await Unauthorized("Include token", ctx); return; }

            db.AuthTokens.Remove(authToken);
            await db.SaveChangesAsync();

            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsync("Success");
        }

        private static async Task WithToken(HttpContext ctx, ValourDB db)
        {
            string token = await ctx.Request.ReadBodyStringAsync();

            var authToken = await db.AuthTokens.Include(x => x.User).FirstOrDefaultAsync(x => x.Id == token);

            if (authToken == null) { await NotFound("User not found", ctx); return; }

            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsJsonAsync(authToken.User);
        }

        private static async Task RequestToken(HttpContext ctx, ValourDB db, UserManager userManager)
        {
            TokenRequest request = await JsonSerializer.DeserializeAsync<TokenRequest>(ctx.Request.Body);

            UserEmail emailObj = await db.UserEmails.Include(x => x.User).FirstOrDefaultAsync(x => x.Email == request.Email.ToLower());

            if (emailObj == null) { await Unauthorized("Failed to authorize", ctx); return; }

            if (emailObj.User.Disabled) { await BadRequest("User is disabled.", ctx); return; }

            if (!emailObj.Verified) { await Unauthorized("The email associated with this account needs to be verified! Please check your email.", ctx); return; }

            var result = await userManager.ValidateAsync(CredentialType.PASSWORD, request.Email, request.Password);

            if (!result.Success) { await Unauthorized("Failed to authorize", ctx); return; }

            if (emailObj.User.Id != result.Data.Id) { await Unauthorized("Failed to authorize", ctx); return; } // This would be weird tbhtbh

            // Attempt to re-use token
            var token = await db.AuthTokens.FirstOrDefaultAsync(x => x.App_Id == "VALOUR" && x.User_Id == emailObj.User_Id && x.Scope == UserPermissions.FullControl.Value);

            if (token is null)
            {
                // We now have to create a token for the user
                token = new AuthToken()
                {
                    App_Id = "VALOUR",
                    Id = "val-" + Guid.NewGuid().ToString(),
                    Created = DateTime.UtcNow,
                    Expires = DateTime.UtcNow.AddDays(7),
                    Scope = UserPermissions.FullControl.Value,
                    User_Id = emailObj.User_Id
                };

                await db.AuthTokens.AddAsync(token);
                await db.SaveChangesAsync();
            }
            else
            {
                token.Created = DateTime.UtcNow;
                token.Expires = DateTime.UtcNow.AddDays(7);

                db.AuthTokens.Update(token);
                await db.SaveChangesAsync();
            }

            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsync(token.Id);
        }

        private static async Task RecoverPassword(HttpContext ctx, ValourDB db, [FromBody] PasswordRecoveryRequest request)
        {
            if (request == null) { await BadRequest("Include request data in body", ctx); return; }

            PasswordRecovery recovery = await db.PasswordRecoveries.FindAsync(request.Code);

            if (recovery == null) { await NotFound("Recovery request not found", ctx); return; }

            TaskResult passwordValid = UserUtils.TestPasswordComplexity(request.Password);

            if (!passwordValid.Success) { await BadRequest(passwordValid.Message, ctx); return; }

            // Get user's old credentials
            Credential credential = await db.Credentials.FirstOrDefaultAsync(x => x.User_Id == recovery.User_Id && x.CredentialType == CredentialType.PASSWORD);

            if (credential == null) { await NotFound("No password-type credentials found. Do you log in via third party service?", ctx); return; }

            // Remove recovery code
            db.PasswordRecoveries.Remove(recovery);

            // Modify old credentials

            // Generate salt
            byte[] salt = PasswordManager.GenerateSalt();

            // Generate password hash
            byte[] hash = PasswordManager.GetHashForPassword(request.Password, salt);

            credential.Salt = salt;
            credential.Secret = hash;

            db.Credentials.Update(credential);
            await db.SaveChangesAsync();

            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsync("Success");
        }

        private static async Task RegisterUser(HttpContext ctx, ValourDB db, string username, string email, string password, string referrer)
        {
            if (username != null)
                username = username.TrimEnd();

            // Prevent duplicates
            if (await db.Users.AnyAsync(x => x.Name.ToLower() == username.ToLower())) { await BadRequest("Username taken", ctx); return; }

            if (await db.UserEmails.AnyAsync(x => x.Email.ToLower() == email.ToLower())) { await BadRequest("Email taken", ctx); return; }

            // Test email
            TaskResult<string> emailResult = UserUtils.TestEmail(email);

            if (!emailResult.Success) { await BadRequest(emailResult.Message, ctx); return; }

            // This may fix broken email formatting
            email = emailResult.Data;

            // Test username
            TaskResult usernameResult = UserUtils.TestUsername(username);

            if (!usernameResult.Success) { await BadRequest(usernameResult.Message, ctx); return; }

            // Test password complexity
            TaskResult passwordResult = UserUtils.TestPasswordComplexity(password);

            // Enforce password tests
            if (!passwordResult.Success) { await BadRequest(passwordResult.Message, ctx); return; }


            // Manage referral
            Database.Items.Users.Referral refer = null;

            if (!string.IsNullOrWhiteSpace(referrer))
            {
                User referUser = await db.Users.FirstOrDefaultAsync(x => x.Name.ToLower() == referrer.ToLower());

                if (referUser == null) { await BadRequest($"Could not find referrer {referrer}", ctx); return; }

                refer = new Database.Items.Users.Referral() { Referrer_Id = referUser.Id };
            }

            // At this point the safety checks are complete

            // Generate random salt
            byte[] salt = PasswordManager.GenerateSalt();

            // Generate password hash
            byte[] hash = PasswordManager.GetHashForPassword(password, salt);

            // Create user object
            User user = new User()
            {
                Id = IdManager.Generate(),
                Name = username,
                Joined = DateTime.UtcNow,
                LastActive = DateTime.UtcNow
            };

            // An error here would be really bad so we'll be careful and catch any exceptions
            try
            {
                await db.Users.AddAsync(user);
                await db.SaveChangesAsync();
            }
            catch (System.Exception e)
            {
                Console.WriteLine(e.Message);
                await BadRequest("A critical error occured adding the user.", ctx);
                return;
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
                await db.UserEmails.AddAsync(emailObj);
                await db.SaveChangesAsync();
            }
            catch (System.Exception e)
            {
                Console.WriteLine(e.Message);
                await BadRequest("A critical error occured adding the email.", ctx);
                return;
            }

            Credential cred = new Credential()
            {
                CredentialType = CredentialType.PASSWORD,
                Identifier = email,
                Salt = salt,
                Secret = hash,
                User_Id = user.Id // We need to find what the user's assigned ID is (auto-filled by EF)
            };

            // An error here would be really bad so we'll be careful and catch any exceptions
            try
            {
                await db.Credentials.AddAsync(cred);
                await db.SaveChangesAsync();
            }
            catch (System.Exception e)
            {
                Console.WriteLine(e.Message);
                await BadRequest("A critical error occured adding the credentials.", ctx);
                return;
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
                await db.Referrals.AddAsync(refer);
                await db.SaveChangesAsync();
            }

            // An error here would be really bad so we'll be careful and catch any exceptions
            try
            {
                await db.EmailConfirmCodes.AddAsync(emailConfirm);
                await db.SaveChangesAsync();
            }
            catch (System.Exception e)
            {
                Console.WriteLine(e.Message);
                await BadRequest("A critical error occured adding the email confirmation code.", ctx);
                return;
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
                                <a href='https://valour.gg/verify/{code}'>Verify</a>
                              </p>
                            </body>";

            string rawmsg = $"Welcome to Valour!\nTo verify your new account, please go to the following link:\nhttps://valour.gg/verify/{code}";

            await EmailManager.SendEmailAsync(email, "Valour Registration", rawmsg, emsg);

            // Add user to main Valour server

            PlanetMember member = new()
            {
                Planet_Id = 735703679107072,
                User_Id = user.Id,
                Id = IdManager.Generate(),
                Nickname = user.Name
            };

            db.PlanetMembers.Add(member);

            await db.SaveChangesAsync();

            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsync("Success");
        }

        private static async Task PasswordReset(HttpContext ctx, ValourDB db, [FromBody] string email)
        {
            UserEmail userEmail = await db.UserEmails.FindAsync(email.ToLower());

            if (userEmail == null) { await NotFound("No account found for email", ctx); return; }

            // If a recovery already exists for this user, remove it
            var old = db.PasswordRecoveries.Where(x => x.User_Id == userEmail.User_Id);
            if (old.Count() > 0)
            {
                db.PasswordRecoveries.RemoveRange(old);
                await db.SaveChangesAsync();
            }

            string recoveryCode = Guid.NewGuid().ToString();

            PasswordRecovery recovery = new PasswordRecovery()
            {
                Code = recoveryCode,
                User_Id = userEmail.User_Id
            };

            await db.PasswordRecoveries.AddAsync(recovery);
            await db.SaveChangesAsync();

            // Send registration email
            string emsg = $@"<body>
                              <h2 style='font-family:Helvetica;'>
                                Valour Password Recovery
                              </h2>
                              <p style='font-family:Helvetica;>
                                If you did not request this email, please ignore it.
                                To reset your password, please use the following link: 
                              </p>
                              <p style='font-family:Helvetica;'>
                                <a href='https://valour.gg/RecoverPassword/{recoveryCode}'>Click here to recover</a>
                              </p>
                            </body>";

            string rawmsg = $"To reset your password, please go to the following link:\nhttps://valour.gg/RecoverPassword/{recoveryCode}";

            await EmailManager.SendEmailAsync(email, "Valour Password Recovery", rawmsg, emsg);

            Console.WriteLine($"Sent recovery email to {email}");

            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsync("Email sent");
        }

        private static async Task GetPlanets(HttpContext ctx, ValourDB db, ulong user_id,
                                            [FromHeader] string authorization)
        {
            var auth = await AuthToken.TryAuthorize(authorization, db);

            if (auth == null) { await TokenInvalid(ctx); return; }
            if (!auth.HasScope(UserPermissions.Membership)) { await Unauthorized("Token lacks UserPermissions.Membership", ctx); return; }
            if (auth.User_Id != user_id) { await Unauthorized("User id does not match token holder", ctx); return; }

            User user = await db.Users
                .Include(x => x.Membership)
                .ThenInclude(x => x.Planet)
                .FirstOrDefaultAsync(x => x.Id == user_id);

            if (user == null) { await NotFound("User not found", ctx); return; }

            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsJsonAsync(user.Membership.Select(x => x.Planet));
        }

        private static async Task GetPlanetIds(HttpContext ctx, ValourDB db, ulong user_id,
                                            [FromHeader] string authorization)
        {
            var auth = await AuthToken.TryAuthorize(authorization, db);

            if (auth == null) { await TokenInvalid(ctx); return; }
            if (!auth.HasScope(UserPermissions.Membership)) { await Unauthorized("Token lacks UserPermissions.Membership", ctx); return; }
            if (auth.User_Id != user_id) { await Unauthorized("User id does not match token holder", ctx); return; }

            User user = await db.Users
                .Include(x => x.Membership)
                .FirstOrDefaultAsync(x => x.Id == user_id);

            if (user == null) { await NotFound("User not found", ctx); return; }

            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsJsonAsync(user.Membership.Select(x => x.Planet_Id));
        }

    }
}