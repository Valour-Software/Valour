using SendGrid;
using Microsoft.AspNetCore.Mvc;
using Valour.Server.Database.Items.Authorization;
using Valour.Server.Database.Items.Planets.Members;
using Valour.Server.Database.Users.Identity;
using Valour.Server.Email;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Items.Users;

namespace Valour.Server.Database.Items.Users;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */
[Table("users")]
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
    [Column("pfp_url")]
    public string PfpUrl { get; set; }

    /// <summary>
    /// The Date and Time that the user joined Valour
    /// </summary>
    [Column("time_joined")]
    public DateTime TimeJoined { get; set; }

    /// <summary>
    /// The name of this user
    /// </summary>
    [Column("name")]
    public string Name { get; set; }

    /// <summary>
    /// True if the user is a bot
    /// </summary>
    [Column("bot")]
    public bool Bot { get; set; }

    /// <summary>
    /// True if the account has been disabled
    /// </summary>
    [Column("disabled")]
    public bool Disabled { get; set; }

    /// <summary>
    /// True if this user is a member of the Valour official staff team. Falsely modifying this 
    /// through a client modification to present non-official staff as staff is a breach of our
    /// license. Don't do that.
    /// </summary>
    [Column("valour_staff")]
    public bool ValourStaff { get; set; }

    /// <summary>
    /// The user's currently set status - this could represent how they feel, their disdain for the political climate
    /// of the modern world, their love for their mother's cooking, or their hate for lazy programmers.
    /// </summary>
    [Column("status")]
    public string Status { get; set; }

    /// <summary>
    /// The integer representation of the current user state
    /// </summary>
    [Column("user_state_code")]
    public int UserStateCode { get; set; }

    /// <summary>
    /// The last time this user was flagged as active (successful auth)
    /// </summary>
    [Column("time_last_active")]
    public DateTime TimeLastActive { get; set; }

    /// <summary>
    /// The span of time from which the user was last active
    /// </summary>
    [NotMapped]
    [JsonIgnore]
    public TimeSpan LastActiveSpan =>
        ISharedUser.GetLastActiveSpan(this);

    /// <summary>
    /// The current activity state of the user
    /// </summary>
    [NotMapped]
    [JsonIgnore]
    public UserState UserState
    {
        get => ISharedUser.GetUserState(this);
        set => ISharedUser.SetUserState(this, value);
    }

    /// <summary>
    /// Retrieves a user for the given id
    /// </summary>
    public static async Task<User> FindAsync(long id, ValourDB db) =>
        await FindAsync<User>(id, db);

    /// <summary>
    /// Nuke it.
    /// </summary>
    public async Task HardDelete(ValourDB db)
    {
        var tran = await db.Database.BeginTransactionAsync();

        // Remove messages
        var pMsgs = db.PlanetMessages.Where(x => x.AuthorUserId == Id);
        db.PlanetMessages.RemoveRange(pMsgs);

        // Direct Message Channels
        var dChannels = await db.DirectChatChannels.Where(x => x.UserOneId == Id || x.UserTwoId == Id).ToListAsync();

        foreach (var dc in dChannels)
        {
            // channel states
            var st = db.UserChannelStates.Where(x => x.ChannelId == dc.Id);
            db.UserChannelStates.RemoveRange(st);

            // messages
            var dMsgs = db.DirectMessages.Where(x => x.ChannelId == dc.Id);
            db.DirectMessages.RemoveRange(dMsgs);

            await db.SaveChangesAsync();
        }

        db.DirectChatChannels.RemoveRange(dChannels);
        

        // Remove friends and friend requests
        var requests = db.UserFriends.Where(x => x.UserId == Id || x.FriendId == Id);
        db.UserFriends.RemoveRange(requests);

        // Remove email confirm codes
        var codes = db.EmailConfirmCodes.Where(x => x.UserId == Id);
        db.EmailConfirmCodes.RemoveRange(codes);


        // Remove user emails
        var emails = db.UserEmails.Where(x => x.UserId == Id);
        db.UserEmails.RemoveRange(emails);

        // Remove credentials
        var creds = db.Credentials.Where(x => x.UserId == Id);
        db.Credentials.RemoveRange(creds);

        var recovs = db.PasswordRecoveries.Where(x => x.UserId == Id);
        db.PasswordRecoveries.RemoveRange(recovs);

        // Remove membership stuff
        var pRoles = db.PlanetRoleMembers.Where(x => x.UserId == Id);
        db.PlanetRoleMembers.RemoveRange(pRoles);

        // Remove planet membership
        var members = db.PlanetMembers.Where(x => x.UserId == Id);
        db.PlanetMembers.RemoveRange(members);

        await db.SaveChangesAsync();

        // Node connections
        var nodecs = db.PrimaryNodeConnections.Where(x => x.UserId == Id);
        db.PrimaryNodeConnections.RemoveRange(nodecs);

        // Authtokens
        var tokens = db.AuthTokens.Where(x => x.UserId == Id);
        db.AuthTokens.RemoveRange(tokens);

        // Referrals
        var refer = db.Referrals.Where(x => x.UserId == Id || x.ReferrerId == Id);
        db.Referrals.RemoveRange(refer);

        // Notifications
        var nots = db.NotificationSubscriptions.Where(x => x.UserId == Id);

        // Bans
        var bans = db.PlanetBans.Where(x => x.IssuerId == Id || x.TargetId == Id);
        db.PlanetBans.RemoveRange(bans);

        // Channel states
        var states = db.UserChannelStates.Where(x => x.UserId == Id);
        db.UserChannelStates.RemoveRange(states);

        // Planet invites
        var invites = db.PlanetInvites.Where(x => x.IssuerId == Id);
        db.PlanetInvites.RemoveRange(invites);

        await db.SaveChangesAsync();

        db.Users.Remove(this);
        await db.SaveChangesAsync();

        try
        {
            await tran.CommitAsync();
            Console.WriteLine("Deleting " + this.Name);
        }
        catch(System.Exception e)
        {
            Console.WriteLine("Error Hard Deleting User!");
            Console.WriteLine(e.Message);
        }
    }


    #region Routes

    [ValourRoute(HttpVerbs.Get), TokenRequired, InjectDb]
    public static async Task<IResult> GetUserRouteAsync(HttpContext ctx, long id)
    {
        var db = ctx.GetDb();
        var user = await FindAsync<User>(id, db);

        if (user is null)
            return ValourResult.NotFound<User>();

        return Results.Json(user);
    }

    [ValourRoute(HttpVerbs.Put), TokenRequired, InjectDb]
    [UserPermissionsRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> PutRouteAsync(HttpContext ctx, [FromBody] User user,
        ILogger<User> logger)
    {
        var token = ctx.GetToken();
        var db = ctx.GetDb();

        // Unlike most other entities, we are just copying over a few fields here and
        // ignoring the rest. There are so many things that *should not* be touched by
        // the API it's smarter to just only do what *should*

        if (user.Id != token.UserId)
            return ValourResult.Forbid("You can only change your own user info.");

        var old = await FindAsync<User>(user.Id, db);

        if (user.Status.Length > 64)
            return ValourResult.BadRequest("Max status length is 64 characters.");

        old.Status = user.Status;

        if (user.UserStateCode > 4)
            return ValourResult.BadRequest($"User state {user.UserStateCode} does not exist.");

        old.UserStateCode = user.UserStateCode;

        try
        {
            await db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            logger.LogError(e.Message);
            return ValourResult.Problem(e.Message);
        }

        PlanetHub.NotifyUserChange(old, db);

        return Results.Json(user);
    }

    // This HAS to be GET so that we can forward it from the generic valour.gg domain
    [ValourRoute(HttpVerbs.Get, "/verify/{code}"), InjectDb]
    public static async Task<IResult> VerifyEmailRouteAsync(HttpContext ctx, string code,
        ILogger<User> logger)
    {
        var db = ctx.GetDb();

        var confirmCode = await db.EmailConfirmCodes
            .Include(x => x.User)
            .ThenInclude(x => x.Email)
            .FirstOrDefaultAsync(x => x.Code == code);

        if (confirmCode is null)
            return ValourResult.NotFound<EmailConfirmCode>();

        using var tran = await db.Database.BeginTransactionAsync();

        try
        {
            confirmCode.User.Email.Verified = true;
            db.EmailConfirmCodes.Remove(confirmCode);
            await db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            await tran.RollbackAsync();
            logger.LogError(e.Message);
            return ValourResult.Problem(e.Message);
        }

        await tran.CommitAsync();

        return Results.LocalRedirect("/FromVerify", true, false);
    }

    [ValourRoute(HttpVerbs.Post, "/self/logout"), TokenRequired, InjectDb]
    public static async Task<IResult> LogOutRouteAsync(HttpContext ctx,
        ILogger<User> logger)
    {
        var token = ctx.GetToken();
        var db = ctx.GetDb();

        try
        {
            db.Entry(token).State = EntityState.Deleted;
            AuthToken.QuickCache.Remove(token.Id, out _);
            await db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            logger.LogError(e.Message);
            return ValourResult.Problem(e.Message);
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

    [ValourRoute(HttpVerbs.Get, "/self/channelstates"), TokenRequired, InjectDb]
    public static IResult ChannelStatesRouteAsync(HttpContext ctx)
    {
        var token = ctx.GetToken();
        var db = ctx.GetDb();

        var channelStates = db.UserChannelStates.Where(x => x.UserId == token.UserId).AsAsyncEnumerable();

        return Results.Json(channelStates);
    }

    [ValourRoute(HttpVerbs.Post, "/token"), InjectDb]
    public static async Task<IResult> GetTokenRouteAsync(HttpContext ctx, [FromBody] TokenRequest tokenRequest,
        ILogger<User> logger)
    {
        var db = ctx.GetDb();

        if (tokenRequest is null)
            return ValourResult.BadRequest("Include request in body.");

        UserEmail userEmail = await db.UserEmails
            .Include(x => x.User)
            .FirstOrDefaultAsync(x => x.Email.ToLower() == tokenRequest.Email.ToLower());

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
            .FirstOrDefaultAsync(x => x.AppId == "VALOUR" &&
                                      x.UserId == userEmail.UserId &&
                                      x.Scope == UserPermissions.FullControl.Value);

        try
        {
            if (token is null)
            {
                // We now have to create a token for the user
                token = new AuthToken()
                {
                    AppId = "VALOUR",
                    Id = "val-" + Guid.NewGuid().ToString(),
                    TimeCreated = DateTime.UtcNow,
                    TimeExpires = DateTime.UtcNow.AddDays(7),
                    Scope = UserPermissions.FullControl.Value,
                    UserId = userEmail.UserId,
                    IssuedAddress = ctx.Connection.RemoteIpAddress.ToString()
                };

                await db.AuthTokens.AddAsync(token);
                await db.SaveChangesAsync();
            }
            else
            {
                token.TimeCreated = DateTime.UtcNow;
                token.TimeExpires = DateTime.UtcNow.AddDays(7);

                db.AuthTokens.Update(token);
                await db.SaveChangesAsync();
            }
        }
        catch (Exception e)
        {
            logger.LogError(e.Message);
            return ValourResult.Problem(e.Message);
        }

        return Results.Json(token);
    }

    [ValourRoute(HttpVerbs.Post, "/self/recovery"), InjectDb]
    public static async Task<IResult> RecoverPasswordRouteAsync(HttpContext ctx, [FromBody] PasswordRecoveryRequest request,
        ILogger<User> logger)
    {
        var db = ctx.GetDb();

        if (request is null)
            return ValourResult.BadRequest("Include request in body.");

        var recovery = await db.PasswordRecoveries.FirstOrDefaultAsync(x => x.Code == request.Code);
        if (recovery is null)
            return ValourResult.NotFound<PasswordRecovery>();

        var passValid = UserUtils.TestPasswordComplexity(request.Password);
        if (!passValid.Success)
            return ValourResult.BadRequest(passValid.Message);

        // Old credentialsto set 
        Credential cred = await db.Credentials.FirstOrDefaultAsync(x => x.UserId == recovery.UserId);
        if (cred is null)
            return ValourResult.BadRequest("No old credentials found. Do you log in via third party service (Like Google)?");

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
        catch (Exception e)
        {
            logger.LogError(e.Message);
            return ValourResult.Problem("We're sorry. Something unexpected occured. Try again?");
        }

        await tran.CommitAsync();

        return Results.NoContent();
    }

    [ValourRoute(HttpVerbs.Post, "/register"), InjectDb]
    public static async Task<IResult> RegisterUserRouteAsync(HttpContext ctx, [FromBody] RegisterUserRequest request,
        ILogger<User> logger)
    {
        var db = ctx.GetDb();

        if (request is null)
            return ValourResult.BadRequest("Include request in body");

        // Prevent trailing whitespace
        request.Username = request.Username.Trim();
        // Prevent comparisons issues
        request.Email = request.Email.ToLower();

        if (await db.Users.AnyAsync(x => x.Name.ToLower() == request.Username.ToLower()))
            return ValourResult.BadRequest("Username is taken");

        if (await db.UserEmails.AnyAsync(x => x.Email.ToLower() == request.Email))
            return ValourResult.BadRequest("This email has already been used");

        var emailValid = UserUtils.TestEmail(request.Email);
        if (!emailValid.Success)
            return ValourResult.BadRequest(emailValid.Message);

        // Check for whole blocked emails
        if (await db.BlockedUserEmails.AnyAsync(x => x.Email.ToLower() == request.Email.ToLower()))
            return ValourResult.BadRequest("Include request in body"); // Vague on purpose

        var host = request.Email.Split('@')[1];

        // Check for blocked host
        if (await db.BlockedUserEmails.AnyAsync(x => x.Email.ToLower() == host.ToLower()))
            return ValourResult.BadRequest("Include request in body"); // Vague on purpose


        var usernameValid = UserUtils.TestUsername(request.Username);
        if (!usernameValid.Success)
            return ValourResult.BadRequest(usernameValid.Message);

        var passwordValid = UserUtils.TestPasswordComplexity(request.Password);
        if (!passwordValid.Success)
            return ValourResult.BadRequest(passwordValid.Message);

        Referral refer = null;
        if (request.Referrer != null && !string.IsNullOrWhiteSpace(request.Referrer))
        {
            request.Referrer = request.Referrer.Trim();
            var referUser = await db.Users.FirstOrDefaultAsync(x => x.Name.ToLower() == request.Referrer.ToLower());
            if (referUser is null)
                return ValourResult.NotFound("Referrer not found");

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
                TimeJoined = DateTime.UtcNow,
                TimeLastActive = DateTime.UtcNow,
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
                Id = IdManager.Generate(),
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

            Response result = await SendRegistrationEmail(ctx.Request, request.Email, emailCode);

            if (!result.IsSuccessStatusCode)
            {
                logger.LogError($"Issue sending email to {request.Email}. Error code {result.StatusCode}.");
                await tran.RollbackAsync();
                return ValourResult.Problem("Sorry! We had an issue emailing your confirmation. Try again?");
            }
        }
        catch (Exception e)
        {
            await tran.RollbackAsync();
            logger.LogError(e.Message);
            return ValourResult.Problem("Sorry! An unexpected error occured. Try again?");
        }

        await tran.CommitAsync();

        return Results.Ok("Your confirmation email has been sent!");
    }

    [ValourRoute(HttpVerbs.Post, "/resendemail"), InjectDb]
    public static async Task<IResult> ResendRegistrationEmail(HttpContext ctx, [FromBody] RegisterUserRequest request,
        ILogger<User> logger)
    {
        var db = ctx.GetDb();

        if (request is null)
            return ValourResult.BadRequest("Include request in body");

        UserEmail userEmail = await db.UserEmails.FindAsync(request.Email);

        if (userEmail is null)
            return ValourResult.NotFound("Could not find user. Retry registration?");

        if (userEmail.Verified)
            return Results.Ok("You are already verified, you can close this!");

        using var tran = await db.Database.BeginTransactionAsync();

        try
        {
            db.EmailConfirmCodes.RemoveRange(db.EmailConfirmCodes.Where(x => x.UserId == userEmail.UserId));

            var emailCode = Guid.NewGuid().ToString();
            EmailConfirmCode confirmCode = new()
            {
                Code = emailCode,
                UserId = userEmail.UserId
            };

            await db.EmailConfirmCodes.AddAsync(confirmCode);
            await db.SaveChangesAsync();

            Response result = await SendRegistrationEmail(ctx.Request, request.Email, emailCode);
            if (!result.IsSuccessStatusCode)
            {
                logger.LogError($"Issue sending email to {request.Email}. Error code {result.StatusCode}.");
                await tran.RollbackAsync();
                return ValourResult.Problem("Sorry! We had an issue emailing your confirmation. Try again?");
            }
        }
        catch (Exception e)
        {
            await tran.RollbackAsync();
            logger.LogError(e.Message);
            return ValourResult.Problem("Sorry! An unexpected error occured. Try again?");
        }

        await tran.CommitAsync();

        return ValourResult.Ok("Confirmation email has been resent!");
    }

    private static async Task<Response> SendRegistrationEmail(HttpRequest request, string email, string code)
    {
        var host = request.Host.ToUriComponent();
        string link = $"{request.Scheme}://{host}/api/user/verify/{code}";

        string emsg = $@"<body>
                                  <h2 style='font-family:Helvetica;'>
                                    Welcome to Valour!
                                  </h2>
                                  <p style='font-family:Helvetica;>
                                    To verify your new account, please use the following link: 
                                  </p>
                                  <p style='font-family:Helvetica;'>
                                    <a href='{link}'>Verify</a>
                                  </p>
                                </body>";

        string rawmsg = $"Welcome to Valour!\nTo verify your new account, please go to the following link:\n{link}";

        var result = await EmailManager.SendEmailAsync(email, "Valour Registration", rawmsg, emsg);
        return result;
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
            if (oldRecoveries.Any())
            {
                db.PasswordRecoveries.RemoveRange(oldRecoveries);
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

            var host = ctx.Request.Host.ToUriComponent();
            string link = $"{ctx.Request.Scheme}://{host}/RecoverPassword/{recoveryCode}";

            string emsg = $@"<body>
                              <h2 style='font-family:Helvetica;'>
                                Valour Password Recovery
                              </h2>
                              <p style='font-family:Helvetica;>
                                If you did not request this email, please ignore it.
                                To reset your password, please use the following link: 
                              </p>
                              <p style='font-family:Helvetica;'>
                                <a href='{link}'>Click here to recover</a>
                              </p>
                            </body>";

            string rawmsg = $"To reset your password, please go to the following link:\n{link}";

            var result = await EmailManager.SendEmailAsync(email, "Valour Password Recovery", rawmsg, emsg);

            if (!result.IsSuccessStatusCode)
            {
                logger.LogError($"Error issuing password reset email to {email}. Status code {result.StatusCode}.");
                return ValourResult.Problem("Sorry! There was an issue sending the email. Try again?");
            }
        }
        catch (Exception e)
        {
            logger.LogError(e.Message);
            return ValourResult.Problem("Sorry! An unexpected error occured. Try again?");
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

    [ValourRoute(HttpVerbs.Get, "/{id}/friends"), TokenRequired, InjectDb]
    [UserPermissionsRequired(UserPermissionsEnum.Friends)]
    public static async Task<IResult> GetFriendsRouteAsync(HttpContext ctx, long id)
    {
        var token = ctx.GetToken();
        var db = ctx.GetDb();

        if (id != token.UserId)
            return ValourResult.Forbid("You cannot currently view another user's friends.");

        // Users added by this user as a friend (user -> other)
        var added = db.UserFriends.Where(x => x.UserId == id);

        // Users who added this user as a friend (other -> user)
        var addedBy = db.UserFriends.Where(x => x.FriendId == id);

        // Mutual friendships
        var mutual = added.Select(x => x.FriendId).Intersect(addedBy.Select(x => x.UserId));

        var friends = await db.Users.Where(x => mutual.Contains(x.Id)).ToListAsync();

        return Results.Json(friends);
    }

    [ValourRoute(HttpVerbs.Get, "/{id}/frienddata"), TokenRequired, InjectDb]
    [UserPermissionsRequired(UserPermissionsEnum.Friends)]
    public static async Task<IResult> GetFriendDataRouteAsync(HttpContext ctx, long id)
    {
        var token = ctx.GetToken();
        var db = ctx.GetDb();

        if (id != token.UserId)
            return ValourResult.Forbid("You cannot currently view another user's friend data.");

        // Users added by this user as a friend (user -> other)
        var added = await db.UserFriends.Include(x => x.Friend).Where(x => x.UserId == id).Select(x => x.Friend).ToListAsync();

        // Users who added this user as a friend (other -> user)
        var addedBy = await db.UserFriends.Include(x => x.User).Where(x => x.FriendId == id).Select(x => x.User).ToListAsync();

        List<User> usersAdded = new();
        List<User> usersAddedBy = new();

        return Results.Json(new
        {
            added = added,
            addedBy = addedBy
        });
    }

    #endregion
}

