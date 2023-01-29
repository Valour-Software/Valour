using IdGen;
using Newtonsoft.Json.Linq;
using SendGrid;
using Valour.Database.Context;
using Valour.Server.Database;
using Valour.Server.Email;
using Valour.Server.Users;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Server.Services;

public class UserService
{
    private readonly ValourDB _db;
    private readonly TokenService _tokenService;
    private readonly ILogger<UserService> _logger;
    private readonly CoreHubService _coreHub;

    /// <summary>
    /// The stored user for the current request
    /// </summary>
    private User _currentUser;

    public UserService(
        ValourDB db,
        TokenService tokenService,
        ILogger<UserService> logger,
        CoreHubService coreHub)
    {
        _db = db;
        _tokenService = tokenService;
        _logger = logger;
        _coreHub = coreHub;
    }

    /// <summary>
    /// Returns the user for the given id
    /// </summary>
    public async Task<User> GetAsync(long id) =>
        (await _db.Users.FindAsync(id)).ToModel();

    public async Task<EmailConfirmCode> GetEmailConfirmCode(string code) =>
        (await _db.EmailConfirmCodes.FirstOrDefaultAsync(x => x.Code == code)).ToModel();
    public async Task<List<Planet>> GetPlanetsUserIn(long userId) =>
        await _db.PlanetMembers
            .Where(x => x.UserId == userId)
            .Include(x => x.Planet)
            .Select(x => x.Planet.ToModel())
            .ToListAsync();

    public async Task<PasswordRecovery> GetPasswordRecoveryAsync(string code) =>
        (await _db.PasswordRecoveries.FirstOrDefaultAsync(x => x.Code == code)).ToModel();

    public async Task<Valour.Database.Credential> GetCredentialAsync(long userId) =>
        await _db.Credentials.FirstOrDefaultAsync(x => x.UserId == userId);

    public async Task<List<UserChannelState>> GetUserChannelStatesAsync(long userId) =>
        await _db.UserChannelStates.Where(x => x.UserId == userId).Select(x => x.ToModel()).ToListAsync();

    public async Task<List<TenorFavorite>> GetTenorFavoritesAsync(long userId) =>
        await _db.TenorFavorites.Where(x => x.UserId == userId).Select(x => x.ToModel()).ToListAsync();

    public async Task<(List<User> added, List<User> addedBy)> GetFriendsDataAsync(long userId)
    {
        // Users added by this user as a friend (user -> other)
        var added = await _db.UserFriends.Include(x => x.Friend).Where(x => x.UserId == userId).Select(x => x.Friend.ToModel()).ToListAsync();

        // Users who added this user as a friend (other -> user)
        var addedBy = await _db.UserFriends.Include(x => x.User).Where(x => x.FriendId == userId).Select(x => x.User.ToModel()).ToListAsync();

        return (added, addedBy);
    }

    public async Task<List<User>> GetFriends(long userId)
    {
        // Users added by this user as a friend (user -> other)
        var added = _db.UserFriends.Where(x => x.UserId == userId);

        // Users who added this user as a friend (other -> user)
        var addedBy = _db.UserFriends.Where(x => x.FriendId == userId);

        // Mutual friendships
        var mutual = added.Select(x => x.FriendId).Intersect(addedBy.Select(x => x.UserId));

        var friends = await _db.Users.Where(x => mutual.Contains(x.Id)).Select(x => x.ToModel()).ToListAsync();

        return friends;
    }

    public async Task<UserEmail> GetUserEmailAsync(string email, bool makelowercase = true)
    {
        if (!makelowercase)
            return (await _db.UserEmails.FindAsync(email)).ToModel();
        else
            return (await _db.UserEmails.FirstOrDefaultAsync(x => x.Email.ToLower() == email.ToLower())).ToModel();
    }

    public async Task<TaskResult> SendPasswordResetEmail(UserEmail userEmail, string email, HttpContext ctx)
    {
        try
        {
            var oldRecoveries = _db.PasswordRecoveries.Where(x => x.UserId == userEmail.UserId);
            if (oldRecoveries.Any())
            {
                _db.PasswordRecoveries.RemoveRange(oldRecoveries);
                await _db.SaveChangesAsync();
            }

            string recoveryCode = Guid.NewGuid().ToString();

            PasswordRecovery recovery = new()
            {
                Code = recoveryCode,
                UserId = userEmail.UserId
            };

            await _db.PasswordRecoveries.AddAsync(recovery.ToDatabase());
            await _db.SaveChangesAsync();

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
                _logger.LogError($"Error issuing password reset email to {email}. Status code {result.StatusCode}.");
                return new(false, "Sorry! There was an issue sending the email. Try again?");
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e.Message);
            return new(false, "Sorry! An unexpected error occured. Try again?");
        }

        return new(true, "Success");
    }

    public async Task<TaskResult> RecoveryUserAsync(PasswordRecoveryRequest request, PasswordRecovery recovery, Valour.Database.Credential cred)
    {
        using var tran = await _db.Database.BeginTransactionAsync();

        try
        {
            _db.PasswordRecoveries.Remove(recovery.ToDatabase());

            byte[] salt = PasswordManager.GenerateSalt();
            byte[] hash = PasswordManager.GetHashForPassword(request.Password, salt);

            cred.Salt = salt;
            cred.Secret = hash;

            _db.Credentials.Update(cred);
            await _db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e.Message);
            return new(false, "We're sorry. Something unexpected occured. Try again?");
        }

        await tran.CommitAsync();

        return new(true, "Success");
    }

    public async Task<TaskResult> RegisterUserAsync(RegisterUserRequest request, HttpContext ctx)
    {
        if (await _db.Users.AnyAsync(x => x.Name.ToLower() == request.Username.ToLower()))
            return new(false, "Username is taken");

        if (await _db.UserEmails.AnyAsync(x => x.Email.ToLower() == request.Email))
            return new(false, "This email has already been used");

        var emailValid = UserUtils.TestEmail(request.Email);
        if (!emailValid.Success)
            return new(false, emailValid.Message);

        // Check for whole blocked emails
        if (await _db.BlockedUserEmails.AnyAsync(x => x.Email.ToLower() == request.Email.ToLower()))
            return new(false, "Include request in body"); // Vague on purpose

        var host = request.Email.Split('@')[1];

        // Check for blocked host
        if (await _db.BlockedUserEmails.AnyAsync(x => x.Email.ToLower() == host.ToLower()))
            return new(false, "Include request in body"); // Vague on purpose


        var usernameValid = UserUtils.TestUsername(request.Username);
        if (!usernameValid.Success)
            return new(false, usernameValid.Message);

        var passwordValid = UserUtils.TestPasswordComplexity(request.Password);
        if (!passwordValid.Success)
            return new(false, passwordValid.Message);

        Referral refer = null;
        if (request.Referrer != null && !string.IsNullOrWhiteSpace(request.Referrer))
        {
            request.Referrer = request.Referrer.Trim();
            var referUser = await _db.Users.FirstOrDefaultAsync(x => x.Name.ToLower() == request.Referrer.ToLower());
            if (referUser is null)
                return new(false, "Referrer not found");

            refer = new Referral()
            {
                ReferrerId = referUser.Id
            };
        }

        byte[] salt = PasswordManager.GenerateSalt();
        byte[] hash = PasswordManager.GetHashForPassword(request.Password, salt);

        using var tran = await _db.Database.BeginTransactionAsync();

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

            _db.Users.Add(user.ToDatabase());
            await _db.SaveChangesAsync();

            if (refer != null)
            {
                refer.UserId = user.Id;
                await _db.Referrals.AddAsync(refer.ToDatabase());
            }

            UserEmail userEmail = new()
            {
                Email = request.Email,
                Verified = false,
                UserId = user.Id
            };

            _db.UserEmails.Add(userEmail.ToDatabase());

            Valour.Database.Credential cred = new()
            {
                Id = IdManager.Generate(),
                CredentialType = Valour.Database.CredentialType.PASSWORD,
                Identifier = request.Email,
                Salt = salt,
                Secret = hash,
                UserId = user.Id
            };

            _db.Credentials.Add(cred);

            var emailCode = Guid.NewGuid().ToString();
            EmailConfirmCode confirmCode = new()
            {
                Code = emailCode,
                UserId = user.Id
            };

            _db.EmailConfirmCodes.Add(confirmCode.ToDatabase());
            await _db.SaveChangesAsync();

            Response result = await SendRegistrationEmail(ctx.Request, request.Email, emailCode);

            if (!result.IsSuccessStatusCode)
            {
                _logger.LogError($"Issue sending email to {request.Email}. Error code {result.StatusCode}.");
                await tran.RollbackAsync();
                return new(false, "Sorry! We had an issue emailing your confirmation. Try again?");
            }
        }
        catch (Exception e)
        {
            await tran.RollbackAsync();
            _logger.LogError(e.Message);
            return new(false, "Sorry! An unexpected error occured. Try again?");
        }

        await tran.CommitAsync();

        return new(true, "Success");
    }

    public async Task<TaskResult> ResendRegistrationEmail(UserEmail userEmail, HttpContext ctx, RegisterUserRequest request)
    {
        await using var tran = await _db.Database.BeginTransactionAsync();

        try
        {
            _db.EmailConfirmCodes.RemoveRange(_db.EmailConfirmCodes.Where(x => x.UserId == userEmail.UserId));

            var emailCode = Guid.NewGuid().ToString();
            EmailConfirmCode confirmCode = new()
            {
                Code = emailCode,
                UserId = userEmail.UserId
            };

            _db.EmailConfirmCodes.Add(confirmCode.ToDatabase());
            await _db.SaveChangesAsync();

            Response result = await SendRegistrationEmail(ctx.Request, request.Email, emailCode);
            if (!result.IsSuccessStatusCode)
            {
                _logger.LogError($"Issue sending email to {request.Email}. Error code {result.StatusCode}.");
                await tran.RollbackAsync();
                return new(false, "Sorry! We had an issue emailing your confirmation. Try again?");
            }
        }
        catch (Exception e)
        {
            await tran.RollbackAsync();
            _logger.LogError(e.Message);
            return new(false, "Sorry! An unexpected error occured. Try again?");
        }

        await tran.CommitAsync();

        return new(true, "Success");
    }

    private static async Task<Response> SendRegistrationEmail(HttpRequest request, string email, string code)
    {
        var host = request.Host.ToUriComponent();
        string link = $"{request.Scheme}://{host}/api/users/verify/{code}";

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

    public async Task<TaskResult<User>> UpdateAsync(User updatedUser)
    {
        var old = await GetAsync(updatedUser.Id);

        old.Status = updatedUser.Status;

        old.UserStateCode = updatedUser.UserStateCode;

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e.Message);
            return new(false, e.Message);
        }

        await _coreHub.NotifyUserChange(old);

        return new(true, "Success", old);
    }

    public async Task<TaskResult> VerifyAsync(EmailConfirmCode confirmCode)
    {
        await using var tran = await _db.Database.BeginTransactionAsync();

        try
        {
            var email = await _db.UserEmails.FirstOrDefaultAsync(x => x.UserId == confirmCode.UserId);
            email.Verified = true;
            _db.EmailConfirmCodes.Remove(confirmCode.ToDatabase());
            await _db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            await tran.RollbackAsync();
            _logger.LogError(e.Message);
            return new(false, e.Message);
        }

        await tran.CommitAsync();

        return new(true, "Success");
    }

    public async Task<TaskResult> Logout(AuthToken token)
    {
        try
        {
            _db.Entry(token.ToDatabase()).State = EntityState.Deleted;
            _tokenService.RemoveFromQuickCache(token.Id);
            await _db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e.Message);
            return new(false, e.Message);
        }

        return new(true, "Success");
    }


    /// <summary>
    /// Returns the user for the current context
    /// </summary>

    public async Task<User> GetCurrentUserAsync()
    {
        var token = await _tokenService.GetCurrentToken();
        _currentUser = await GetAsync(token.UserId);
        return _currentUser;
    }
    
    /// <summary>
    /// Returns the user id for the current context
    /// </summary>

    public async Task<long> GetCurrentUserId()
    {
        var token = await _tokenService.GetCurrentToken();
        return token?.UserId ?? long.MinValue;
    }
    
    /// <summary>
    /// Returns the amount of planets owned by the given user
    /// </summary>
    public Task<int> GetOwnedPlanetCount(long userId) => 
        _db.Planets.CountAsync(x => x.OwnerId == userId);
    
    /// <summary>
    /// Returns the amount of planets joined by the given user
    /// </summary>
    public Task<int> GetJoinedPlanetCount(long userId) => 
        _db.PlanetMembers.CountAsync(x => x.UserId == userId);

    public async Task<TaskResult<User>> ValidateAsync(string credential_type, string identifier, string secret)
    {
        // Find the credential that matches the identifier and type
        Valour.Database.Credential credential = await _db.Credentials.FirstOrDefaultAsync(
            x => string.Equals(credential_type.ToUpper(), x.CredentialType.ToUpper()) &&
                    string.Equals(identifier.ToUpper(), x.Identifier.ToUpper()));

        if (credential == null || string.IsNullOrWhiteSpace(secret))
        {
            return new TaskResult<User>(false, "The credentials were incorrect.", null);
        }

        // Use salt to validate secret hash
        byte[] hash = PasswordManager.GetHashForPassword(secret, credential.Salt);

        // Spike needs to remember how reference types work 
        if (!hash.SequenceEqual(credential.Secret))
        {
            return new TaskResult<User>(false, "The credentials were incorrect.", null);
        }

        User user = await GetAsync(credential.UserId);

        if (user.Disabled)
        {
            return new TaskResult<User>(false, "This account has been disabled", null);
        }

        return new TaskResult<User>(true, "Succeeded", user);
    }

    public async Task<TaskResult<AuthToken>> GetTokenAfterLoginAsync(HttpContext ctx, long userId)
    {
        // Check for an old token
        var token = await _db.AuthTokens
            .FirstOrDefaultAsync(x => x.AppId == "VALOUR" &&
                                      x.UserId == userId &&
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
                    UserId = userId,
                    IssuedAddress = ctx.Connection.RemoteIpAddress.ToString()
                }.ToDatabase();

                await _db.AuthTokens.AddAsync(token);
                await _db.SaveChangesAsync();
            }
            else
            {
                token.TimeCreated = DateTime.UtcNow;
                token.TimeExpires = DateTime.UtcNow.AddDays(7);

                _db.Entry(token).State = EntityState.Detached;
                _db.AuthTokens.Update(token);
                await _db.SaveChangesAsync();
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e.Message);
            return new(false, e.Message);
        }

        return new(true, "Success", token.ToModel());
    }

    /// <summary>
    /// Nuke it.
    /// </summary>
    public async Task HardDelete(User user)
    {
        var tran = await _db.Database.BeginTransactionAsync();
        
        var dbUser = await _db.Users.FindAsync(user.Id);
        if (dbUser is null)
            return;

        // Remove messages
        var pMsgs = _db.PlanetMessages.Where(x => x.AuthorUserId == dbUser.Id);
        _db.PlanetMessages.RemoveRange(pMsgs);

        // Direct Message Channels
        var dChannels = await _db.DirectChatChannels
            .Where(x => x.UserOneId == dbUser.Id || x.UserTwoId == dbUser.Id)
            .ToListAsync();

        foreach (var dc in dChannels)
        {
            // channel states
            var st = _db.UserChannelStates.Where(x => x.ChannelId == dc.Id);
            _db.UserChannelStates.RemoveRange(st);

            // messages
            var dMsgs = _db.DirectMessages.Where(x => x.ChannelId == dc.Id);
            _db.DirectMessages.RemoveRange(dMsgs);

            await _db.SaveChangesAsync();
        }

        _db.DirectChatChannels.RemoveRange(dChannels);
        

        // Remove friends and friend requests
        var requests = _db.UserFriends.Where(x => x.UserId == dbUser.Id || x.FriendId == dbUser.Id);
        _db.UserFriends.RemoveRange(requests);

        // Remove email confirm codes
        var codes = _db.EmailConfirmCodes.Where(x => x.UserId == dbUser.Id);
        _db.EmailConfirmCodes.RemoveRange(codes);


        // Remove user emails
        var emails = _db.UserEmails.Where(x => x.UserId == dbUser.Id);
        _db.UserEmails.RemoveRange(emails);

        // Remove credentials
        var creds = _db.Credentials.Where(x => x.UserId == dbUser.Id);
        _db.Credentials.RemoveRange(creds);

        var recovs = _db.PasswordRecoveries.Where(x => x.UserId == dbUser.Id);
        _db.PasswordRecoveries.RemoveRange(recovs);

        // Remove membership stuff
        var pRoles = _db.PlanetRoleMembers.Where(x => x.UserId == dbUser.Id);
        _db.PlanetRoleMembers.RemoveRange(pRoles);

        // Remove planet membership
        var members = _db.PlanetMembers.Where(x => x.UserId == dbUser.Id);
        _db.PlanetMembers.RemoveRange(members);

        await _db.SaveChangesAsync();

        // Authtokens
        var tokens = _db.AuthTokens.Where(x => x.UserId == dbUser.Id);
        _db.AuthTokens.RemoveRange(tokens);

        // Referrals
        var refer = _db.Referrals.Where(x => x.UserId == dbUser.Id || x.ReferrerId == dbUser.Id);
        _db.Referrals.RemoveRange(refer);

        // Notifications
        var nots = _db.NotificationSubscriptions.Where(x => x.UserId == dbUser.Id);

        // Bans
        var bans = _db.PlanetBans.Where(x => x.IssuerId == dbUser.Id || x.TargetId == dbUser.Id);
        _db.PlanetBans.RemoveRange(bans);

        // Channel states
        var states = _db.UserChannelStates.Where(x => x.UserId == dbUser.Id);
        _db.UserChannelStates.RemoveRange(states);

        // Planet invites
        var invites = _db.PlanetInvites.Where(x => x.IssuerId == dbUser.Id);
        _db.PlanetInvites.RemoveRange(invites);

        await _db.SaveChangesAsync();

        _db.Users.Remove(dbUser);
        await _db.SaveChangesAsync();

        try
        {
            await tran.CommitAsync();
            Console.WriteLine("Deleting " + dbUser.Name);
        }
        catch(System.Exception e)
        {
            Console.WriteLine("Error Hard Deleting User!");
            Console.WriteLine(e.Message);
        }
    }
}