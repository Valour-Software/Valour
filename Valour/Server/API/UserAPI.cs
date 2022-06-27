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