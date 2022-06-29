using Microsoft.AspNetCore.Mvc;
using Valour.Server.Database.Items.Authorization;
using Valour.Server.Database.Items.Planets.Members;
using Valour.Server.Database.Users.Identity;
using Valour.Server.Email;
using Valour.Shared.Authorization;
using Valour.Shared.Http;
using Valour.Shared.Items;
using Valour.Shared.Items.Users;

namespace Valour.Server.Database.Items.Users;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

public class User : Item, ISharedUser
{
    [InverseProperty("User")]
    [JsonIgnore]
    public virtual UserEmail Email { get; set; }

    [InverseProperty("User")]
    [JsonIgnore]
    public virtual ICollection<PlanetMember> Membership { get; set; }

    /// <summary>
    /// The url for the user's profile picture
    /// </summary>
    public string PfpUrl { get; set; }

    /// <summary>
    /// The Date and Time that the user joined Valour
    /// </summary>
    public DateTime Joined { get; set; }

    /// <summary>
    /// The name of this user
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// True if the user is a bot
    /// </summary>
    public bool Bot { get; set; }

    /// <summary>
    /// True if the account has been disabled
    /// </summary>
    public bool Disabled { get; set; }

    /// <summary>
    /// True if this user is a member of the Valour official staff team. Falsely modifying this 
    /// through a client modification to present non-official staff as staff is a breach of our
    /// license. Don't do that.
    /// </summary>
    public bool ValourStaff { get; set; }

    /// <summary>
    /// The user's currently set status - this could represent how they feel, their disdain for the political climate
    /// of the modern world, their love for their mother's cooking, or their hate for lazy programmers.
    /// </summary>
    public string Status { get; set; }

    /// <summary>
    /// The integer representation of the current user state
    /// </summary>
    public int UserStateCode { get; set; }

    /// <summary>
    /// The last time this user was flagged as active (successful auth)
    /// </summary>
    public DateTime LastActive { get; set; }

    new public static string ItemType => nameof(User);

    /// <summary>
    /// The span of time from which the user was last active
    /// </summary>
    public TimeSpan LastActiveSpan =>
        ISharedUser.GetLastActiveSpan(this);

    /// <summary>
    /// The current activity state of the user
    /// </summary>
    public UserState UserState
    {
        get => ISharedUser.GetUserState(this);
        set => ISharedUser.SetUserState(this, value);
    }

    #region Routes

    [ValourRoute(HttpVerbs.Get), TokenRequired, InjectDb]
    public static async Task<IResult> GetUserRouteAsync(HttpContext ctx, ulong id)
    {
        var db = ctx.GetDb();
        var user = await FindAsync<User>(id, db);

        if (user is null)
            return ValourResult.NotFound<User>();

        return Results.Json(user);
    }

    [ValourRoute(HttpVerbs.Post, "/verify/{code}"), InjectDb]
    public static async Task<IResult> VerifyEmailRouteAsync(HttpContext ctx, string code,
        ILogger<User> logger)
    {
        var db = ctx.GetDb();
        var token = ctx.GetToken();

        var confirmCode = await db.EmailConfirmCodes
            .Include(x => x.User)
            .ThenInclude(x => x.Email)
            .FirstOrDefaultAsync(x => x.Code == code);

        if (confirmCode is null || token.User.Id != confirmCode.UserId)
            return ValourResult.NotFound<EmailConfirmCode>();

        using var tran = await db.Database.BeginTransactionAsync();

        try
        {
            confirmCode.User.Email.Verified = true;
            db.EmailConfirmCodes.Remove(confirmCode);
            await db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            await tran.RollbackAsync();
            logger.LogError(e.Message);
            return Results.Problem(e.Message);
        }

        await tran.CommitAsync();

        return Results.NoContent();
    }

    [ValourRoute(HttpVerbs.Post, "/self/logout"), TokenRequired, InjectDb]
    public static async Task<IResult> LogOutRouteAsync(HttpContext ctx,
        ILogger<User> logger)
    {
        var token = ctx.GetToken();
        var db = ctx.GetDb();

        try
        {
            db.AuthTokens.Remove(token);
            await db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            logger.LogError(e.Message);
            return Results.Problem(e.Message);
        }

        return Results.Ok("Come back soon!");
    }

    [ValourRoute(HttpVerbs.Get, "/self"), TokenRequired, InjectDb]
    public static async Task<IResult> SelfRouteAsync(HttpContext ctx)
    {
        var token = ctx.GetToken();
        var db = ctx.GetDb();

        var user = await FindAsync<User>(token.UserId, db);

        if (user is null) // This case would be bad for whoever is using this lol
            return ValourResult.NotFound<User>(); // I mean really this should not happen but you know how life is
                                                  // Sometimes things do be wrong

        return Results.Json(user);
    }

    [ValourRoute(HttpVerbs.Get, "/token"), InjectDb]
    public static async Task<IResult> GetTokenRouteAsync(HttpContext ctx, [FromBody] TokenRequest tokenRequest,
        ILogger<User> logger)
    {
        var db = ctx.GetDb();

        if (tokenRequest is null)
            return Results.BadRequest("Include request in body.");

        UserEmail userEmail = await db.UserEmails
            .Include(x => x.User)
            .FirstOrDefaultAsync(x => x.Email == tokenRequest.Email.ToLower());

        if (userEmail is null)
            return ValourResult.InvalidToken();

        if (userEmail.User.Disabled)
            return ValourResult.Forbid("Your account is disabled.");

        if (!userEmail.Verified)
            return ValourResult.Forbid("This account needs email verification. Please check your email.");

        var validResult = await UserManager.ValidateAsync(CredentialType.PASSWORD, tokenRequest.Email, tokenRequest.Password, db);
        if (!validResult.Success)
            return Results.Unauthorized();

        // Check for an old token
        var token = await db.AuthTokens
            .FirstOrDefaultAsync(x => x.App_Id == "VALOUR" &&
                                      x.UserId == userEmail.UserId &&
                                      x.Scope == UserPermissions.FullControl.Value);

        try
        {
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
                    UserId = userEmail.UserId
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
        }
        catch (System.Exception e)
        {
            logger.LogError(e.Message);
            return Results.Problem(e.Message);
        }

        return Results.Json(token);
    }

    [ValourRoute(HttpVerbs.Get, "/self/recovery")]
    public static async Task<IResult> RecoverPasswordRouteAsync(HttpContext ctx, [FromBody] PasswordRecoveryRequest request,
        ILogger<User> logger)
    {
        var db = ctx.GetDb();

        if (request is null)
            return Results.BadRequest("Include request in body.");

        var recovery = await db.PasswordRecoveries.FirstOrDefaultAsync(x => x.Code == request.Code);
        if (recovery is null)
            return ValourResult.NotFound<PasswordRecovery>();

        var passValid = UserUtils.TestPasswordComplexity(request.Password);
        if (!passValid.Success)
            return Results.BadRequest(passValid.Message);

        // Old credentials
        Credential cred = await db.Credentials.FirstOrDefaultAsync(x => x.UserId == recovery.UserId);
        if (cred is null)
            return Results.BadRequest("No old credentials found. Do you log in via third party service (Like Google)?");

        using var tran = await db.Database.BeginTransactionAsync();

        try
        {
            db.PasswordRecoveries.Remove(recovery);

            byte[] salt = PasswordManager.GenerateSalt();
            byte[] hash = PasswordManager.GetHashForPassword(request.Password, salt);

            cred.Salt = salt;
            cred.Secret = hash;

            db.Credentials.Update(cred);
            await db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            logger.LogError(e.Message);
            return Results.Problem("We're sorry. Something unexpected occured. Try again?");
        }

        await tran.CommitAsync();

        return Results.NoContent();
    }

    [ValourRoute(HttpVerbs.Post, "/register")]
    public static async Task<IResult> RegisterUserRouteAsync(HttpContext ctx, [FromBody] RegisterUserRequest request,
        ILogger<User> logger)
    {
        var db = ctx.GetDb();

        if (request is null)
            return Results.BadRequest("Include request in body.");

        // Prevent trailing whitespace
        request.Username = request.Username.Trim();

        if (await db.Users.AnyAsync(x => x.Name.ToLower() == request.Username.ToLower()))
            return Results.BadRequest("Username is taken.");

        if (await db.UserEmails.AnyAsync(x => x.Email.ToLower() == request.Email.ToLower()))
            return Results.BadRequest("This email has already been used.");

        var emailValid = UserUtils.TestEmail(request.Email);
        if (!emailValid.Success)
            return Results.BadRequest(emailValid.Message);

        var usernameValid = UserUtils.TestUsername(request.Username);
        if (!usernameValid.Success)
            return Results.BadRequest(usernameValid.Message);

        var passwordValid = UserUtils.TestPasswordComplexity(request.Password);
        if (!passwordValid.Success)
            return Results.BadRequest(passwordValid.Message);

        Referral refer = null;
        if (!string.IsNullOrWhiteSpace(request.Referrer.Trim()))
        {
            var referUser = await db.Users.FirstOrDefaultAsync(x => x.Name.ToLower() == request.Referrer.ToLower());
            if (referUser is null)
                return Results.BadRequest("Referrer not found.");

            refer = new Referral()
            {
                ReferrerId = referUser.Id
            };
        }

        byte[] salt = PasswordManager.GenerateSalt();
        byte[] hash = PasswordManager.GetHashForPassword(request.Password, salt);


        using var tran = await db.Database.BeginTransactionAsync();

        User user = null;

        try
        {
            user = new()
            {
                Id = IdManager.Generate(),
                Name = request.Username,
                Joined = DateTime.UtcNow,
                LastActive = DateTime.UtcNow,
            };

            await db.Users.AddAsync(user);
            await db.SaveChangesAsync();

            if (refer != null)
            {
                refer.UserId = user.Id;
                await db.Referrals.AddAsync(refer);
            }

            UserEmail userEmail = new()
            {
                Email = request.Email,
                Verified = false,
                UserId = user.Id
            };

            await db.UserEmails.AddAsync(userEmail);

            Credential cred = new()
            {
                CredentialType = CredentialType.PASSWORD,
                Identifier = request.Email,
                Salt = salt,
                Secret = hash,
                UserId = user.Id
            };

            await db.Credentials.AddAsync(cred);

            var emailCode = Guid.NewGuid().ToString();
            EmailConfirmCode confirmCode = new()
            {
                Code = emailCode,
                UserId = user.Id
            };

            await db.EmailConfirmCodes.AddAsync(confirmCode);
            await db.SaveChangesAsync();

            // Send registration email
            string emsg = $@"<body>
                              <h2 style='font-family:Helvetica;'>
                                Welcome to Valour!
                              </h2>
                              <p style='font-family:Helvetica;>
                                To verify your new account, please use the following link: 
                              </p>
                              <p style='font-family:Helvetica;'>
                                <a href='https://valour.gg/api/user/verify/{emailCode}'>Verify</a>
                              </p>
                            </body>";

            string rawmsg = $"Welcome to Valour!\nTo verify your new account, please go to the following link:\nhttps://valour.gg/api/user/verify/{emailCode}";

            var result = await EmailManager.SendEmailAsync(request.Email, "Valour Registration", rawmsg, emsg);
            if (!result.IsSuccessStatusCode)
            {
                logger.LogError($"Issue sending email to {request.Email}. Error code {result.StatusCode}.");
                await tran.RollbackAsync();
                return Results.Problem("Sorry! We had an issue emailing your confirmation. Try again?");
            }
        }
        catch (System.Exception e)
        {
            await tran.RollbackAsync();
            logger.LogError(e.Message);
            return Results.Problem("Sorry! An unexpected error occured. Try again?");
        }

        await tran.CommitAsync();

        return Results.Ok("Your confirmation email has been sent!");
    }

    [ValourRoute(HttpVerbs.Post, "/resetpassword"), InjectDb]
    public static async Task<IResult> ResetPasswordRouteAsync(HttpContext ctx, [FromBody] string email,
        ILogger<User> logger)
    {
        var db = ctx.GetDb();

        var userEmail = await db.UserEmails.FirstOrDefaultAsync(x => x.Email.ToLower() == email.ToLower());

        if (userEmail is null)
            return ValourResult.NotFound<UserEmail>();

        try
        {
            var oldRecoveries = db.PasswordRecoveries.Where(x => x.UserId == userEmail.UserId);
            if (oldRecoveries.Count() > 0)
            {
                await db.PasswordRecoveries.BulkDeleteAsync(oldRecoveries);
                await db.SaveChangesAsync();
            }

            string recoveryCode = Guid.NewGuid().ToString();

            PasswordRecovery recovery = new()
            {
                Code = recoveryCode,
                UserId = userEmail.UserId
            };

            await db.PasswordRecoveries.AddAsync(recovery);
            await db.SaveChangesAsync();

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

            var result = await EmailManager.SendEmailAsync(email, "Valour Password Recovery", rawmsg, emsg);

            if (!result.IsSuccessStatusCode)
            {
                logger.LogError($"Error issuing password reset email to {email}. Status code {result.StatusCode}.");
                return Results.Problem("Sorry! There was an issue sending the email. Try again?");
            }
        }
        catch (System.Exception e)
        {
            logger.LogError(e.Message);
            return Results.Problem("Sorry! An unexpected error occured. Try again?");
        }

        return Results.NoContent();
    }

    [ValourRoute(HttpVerbs.Get, "/self/planets"), TokenRequired, InjectDb]
    [UserPermissionsRequired(UserPermissionsEnum.Membership)]

    public static async Task<IResult> GetPlanetsRouteAsync(HttpContext ctx)
    {
        var token = ctx.GetToken();
        var db = ctx.GetDb();

        var planets = await db.PlanetMembers
            .Where(x => x.UserId == token.UserId)
            .Include(x => x.Planet)
            .Select(x => x.Planet)
            .ToListAsync();

        return Results.Json(planets);
    }

    [ValourRoute(HttpVerbs.Get, "/self/planetids"), TokenRequired, InjectDb]
    [UserPermissionsRequired(UserPermissionsEnum.Membership)]

    public static async Task<IResult> GetPlanetIdsRouteAsync(HttpContext ctx)
    {
        var token = ctx.GetToken();
        var db = ctx.GetDb();

        var planets = await db.PlanetMembers
            .Where(x => x.UserId == token.UserId)
            .Select(x => x.PlanetId)
            .ToListAsync();

        return Results.Json(planets);
    }

    #endregion
}

