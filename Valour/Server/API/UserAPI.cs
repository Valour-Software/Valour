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
            app.MapGet("api/user/{user_id}/planets", GetPlanets);

            app.MapGet("api/user/{user_id}/planet_ids", GetPlanetIds);

            app.MapPost("api/user/register", RegisterUser);

            app.MapPost("api/user/passwordreset", PasswordReset);


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